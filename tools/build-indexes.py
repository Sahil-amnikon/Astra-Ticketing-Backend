"""Convert ordinary and expression indexes; track PostgreSQL search-index replacements."""
from pathlib import Path
import json, re

root = Path(__file__).resolve().parents[1]
review = json.loads((root / 'inventory/sql-schema-review.json').read_text(encoding='utf-8'))

def quoted(name):
    assert re.fullmatch(r'[a-z_][a-z_0-9]*', name), name
    return '[' + name + ']'

def split_keys(value):
    result, start, depth, literal = [], 0, 0, False
    for index, char in enumerate(value):
        if char == "'": literal = not literal
        if not literal:
            if char == '(': depth += 1
            if char == ')': depth -= 1
            if char == ',' and depth == 0: result.append(value[start:index].strip()); start = index + 1
    result.append(value[start:].strip())
    return result

statements = ["SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;\nGO\n"]
converted, pending = [], []
for item in review['indexes']:
    if ' USING btree ' not in item['definition']:
        pending.append(item); continue
    match = re.fullmatch(r'CREATE (UNIQUE )?INDEX (\w+) ON public\.(\w+) USING btree \((.*)\)( WHERE .*)?', item['definition'])
    # The greedy key match also sees parentheses in predicates; split at WHERE first.
    definition, _, predicate = item['definition'].partition(' WHERE ')
    match = re.fullmatch(r'CREATE (UNIQUE )?INDEX (\w+) ON public\.(\w+) USING btree \((.*)\)', definition)
    assert match, item
    keys = []
    for index, expression in enumerate(split_keys(match[4])):
        if re.fullmatch(r'\w+(?: DESC| ASC)?', expression):
            tokens = expression.split(); keys.append(quoted(tokens[0]) + (' ' + tokens[1] if len(tokens)>1 else '')); continue
        expression = re.sub(r"'([^']*)'::uuid", r"CONVERT(uniqueidentifier,'\1')", expression)
        expression = re.sub(r'TRIM\(BOTH FROM (\w+)\)', r'TRIM(\1)', expression)
        computed = 'astra_idx_' + match[2] + '_' + str(index)
        statements.append('ALTER TABLE dbo.' + quoted(match[3]) + ' ADD ' + quoted(computed) + ' AS (' + expression + ') PERSISTED;\nGO\n')
        keys.append(quoted(computed))
    predicate = re.sub(r"::text", '', predicate)
    if predicate == 'active': predicate = '[active]=1'
    for name in re.findall(r'\b([a-z_][a-z_0-9]*)\b', re.sub(r"'[^']*'", '', predicate)):
        if name == 'active': continue
        predicate = re.sub(r'\b' + name + r'\b', quoted(name), predicate)
    statements.append('CREATE ' + (match[1] or '') + 'INDEX ' + quoted(match[2]) + ' ON dbo.' + quoted(match[3]) + '(' + ','.join(keys) + ')' + (' WHERE ' + predicate if predicate else '') + ';\nGO\n')
    converted.append(item['name'])
(root/'database/003_indexes.sql').write_text(''.join(statements),encoding='utf-8')
(root/'inventory/index-conversion.json').write_text(json.dumps({'converted':converted,'pendingSearchIndexes':pending},indent=2),encoding='utf-8')
print(json.dumps({'ordinaryIndexesConverted':len(converted),'pendingSearchIndexes':len(pending)}))
