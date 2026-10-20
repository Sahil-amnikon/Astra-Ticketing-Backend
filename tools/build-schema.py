"""Build a reviewable T-SQL schema from the captured PostgreSQL metadata."""
from pathlib import Path
import collections, json, re

root = Path(__file__).resolve().parents[1]
schema = json.loads((root / 'inventory/schema.json').read_text(encoding='utf-8'))
tables = collections.defaultdict(list)
for column in schema['columns']:
    tables[column['table_name']].append(column)
constraints = [c for c in schema['constraints'] if c['table_name'] in tables]

def q(name):
    assert re.fullmatch(r'[a-z_][a-z_0-9]*', name), name
    return '[' + name + ']'

indexed = set()
for constraint in constraints:
    if constraint['kind'] in ['p', 'u']:
        for name in re.search(r'\((.*?)\)', constraint['definition'])[1].split(','):
            indexed.add((constraint['table_name'], name.strip()))
for index in schema['indexes']:
    if index['table_name'] not in tables or ' USING btree ' not in index['definition']:
        continue
    for column in tables[index['table_name']]:
        if re.search(r'\b' + re.escape(column['column_name']) + r'\b', index['definition'].split(' USING ')[-1]):
            indexed.add((index['table_name'], column['column_name']))

lengths = {'email': 320, 'requester_email': 320, 'external_email': 320, 'domain': 253, 'name': 200, 'code': 64, 'role_code': 64, 'permission_code': 64, 'ticket_prefix': 16, 'prefix': 16, 'mailbox': 320, 'message_id': 450, 'event_key': 450, 'storage_key': 450, 'ticket_number': 64, 'change_number': 64, 'problem_number': 64}
column_types = {}
identity = []
native_defaults = []

def sql_type(c):
    basic = {'uuid': 'uniqueidentifier', 'bool': 'bit', 'int2': 'smallint', 'int4': 'int', 'int8': 'bigint', 'timestamptz': 'datetimeoffset(7)', 'date': 'date', 'time': 'time(7)', 'bytea': 'varbinary(max)', 'inet': 'nvarchar(49)', 'jsonb': 'nvarchar(max)', '_text': 'nvarchar(max)', '_int2': 'nvarchar(max)'}
    if c['udt_name'] in basic:
        return basic[c['udt_name']]
    if c['udt_name'] == 'text':
        if c['table_name']=='tickets' and c['column_name']=='source':
            return 'nvarchar(64)'
        return f"nvarchar({lengths.get(c['column_name'], 450)})" if (c['table_name'], c['column_name']) in indexed else 'nvarchar(max)'
    raise ValueError('Unsupported data type: ' + str(c))

def default(c):
    value = c['column_default']
    if value is None:
        return ''
    if value == 'gen_random_uuid()':
        return ' DEFAULT NEWID()'
    if value in ['now()', 'CURRENT_TIMESTAMP']:
        return ' DEFAULT SYSUTCDATETIME()'
    if value in ['true', 'false']:
        return ' DEFAULT ' + ('1' if value == 'true' else '0')
    if value.startswith('nextval('):
        if c['table_name'] == 'problems' and c['column_name'] == 'number':
            return ' DEFAULT (NEXT VALUE FOR dbo.problem_number_seq)'
        identity.append(c['table_name'] + '.' + c['column_name'])
        return ' IDENTITY(1,1)'
    if "nextval('change_number_seq'" in value:
        native_defaults.append(c['table_name'] + '.' + c['column_name'])
        return ''
    if value == "'{}'::text[]":
        return " DEFAULT N'[]'"
    if re.fullmatch(r'-?\d+', value):
        return ' DEFAULT ' + value
    literal = re.fullmatch(r"('(?:[^']|'')*')::(?:text|bigint|jsonb)", value)
    if literal:
        return ' DEFAULT N' + literal[1] if c['udt_name'] not in ['int8', 'int4', 'int2'] else ' DEFAULT ' + literal[1]
    raise ValueError('Unsupported default: ' + value)

helpers = """SET XACT_ABORT ON;
GO
CREATE FUNCTION dbo.TextLength(@value nvarchar(max)) RETURNS bigint AS
BEGIN RETURN LEN(@value + N'.') - 1; END;
GO
CREATE FUNCTION dbo.JsonArrayCount(@value nvarchar(max)) RETURNS int AS
BEGIN
 IF ISJSON(@value, ARRAY) <> 1 RETURN -1;
 RETURN (SELECT COUNT(*) FROM OPENJSON(@value));
END;
GO
CREATE FUNCTION dbo.JsonContains(@value nvarchar(max), @item nvarchar(450)) RETURNS bit AS
BEGIN
 IF ISJSON(@value, ARRAY) <> 1 RETURN 0;
 IF EXISTS(SELECT 1 FROM OPENJSON(@value) WHERE [type]=1 AND [value]=@item) RETURN 1;
 RETURN 0;
END;
GO
CREATE FUNCTION dbo.JsonSubset(@value nvarchar(max), @allowed nvarchar(max)) RETURNS bit AS
BEGIN
 IF ISJSON(@value, ARRAY) <> 1 OR ISJSON(@allowed, ARRAY) <> 1 RETURN 0;
 IF EXISTS(SELECT 1 FROM OPENJSON(@value) v WHERE v.[type]<>1 OR NOT EXISTS(SELECT 1 FROM OPENJSON(@allowed) a WHERE a.[value]=v.[value])) RETURN 0;
 RETURN 1;
END;
GO
CREATE SEQUENCE dbo.problem_number_seq AS bigint START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE dbo.change_number_seq AS bigint START WITH 1 INCREMENT BY 1;
GO
"""
output = [helpers]
for table, columns in sorted(tables.items()):
    lines = []
    for c in columns:
        if c['is_generated'] == 'ALWAYS':
            assert table == 'problems' and c['column_name'] == 'problem_number'
            lines.append(q(c['column_name']) + " AS (N'PRB-' + REPLICATE(N'0', CASE WHEN LEN(CONVERT(nvarchar(20),[number]))<8 THEN 8-LEN(CONVERT(nvarchar(20),[number])) ELSE 0 END) + CONVERT(nvarchar(20),[number])) PERSISTED")
            continue
        datatype = sql_type(c)
        column_types[table + '.' + c['column_name']] = datatype
        value = q(c['column_name']) + ' ' + datatype + default(c) + (' NULL' if c['is_nullable'] == 'YES' else ' NOT NULL')
        if c['udt_name'] in ['jsonb', '_text', '_int2']:
            kind = 'ARRAY' if c['data_type'] == 'ARRAY' else 'VALUE'
            value += f" CONSTRAINT {q(table + '_' + c['column_name'] + '_json')} CHECK (ISJSON({q(c['column_name'])}, {kind})=1)"
        lines.append(value)
    primary = next(c for c in constraints if c['table_name'] == table and c['kind'] == 'p')
    keys = [q(n.strip()) for n in re.search(r'\((.*?)\)', primary['definition'])[1].split(',')]
    lines.append('CONSTRAINT ' + q(primary['name']) + ' PRIMARY KEY (' + ','.join(keys) + ')')
    output.append('CREATE TABLE dbo.' + q(table) + '(\n  ' + ',\n  '.join(lines) + '\n);\nGO\n')

def check(c):
    value = c['definition'][7:-1]
    value = re.sub(r'::(?:text|bigint|integer|smallint)', '', value)
    value = re.sub(r'TRIM\(BOTH FROM (\w+)\)', r'TRIM(\1)', value)
    value = re.sub(r'\blength\(', 'dbo.TextLength(', value)
    value = re.sub(r'\bcardinality\(', 'dbo.JsonArrayCount(', value)
    value = re.sub(r"(\w+) = ANY \(ARRAY\[(.*?)\]\)", r'\1 IN (\2)', value)
    value = re.sub(r"(\w+) <> ALL \(ARRAY\[(.*?)\]\)", r'\1 NOT IN (\2)', value)
    value = re.sub(r"(\w+) ~ ('[^']*')", r'REGEXP_LIKE(\1, \2)', value)
    value = value.replace(' ~~ ', ' LIKE ')
    value = re.sub(r"('[^']*') = ANY \((\w+)\)", r'dbo.JsonContains(\2, \1)=1', value)
    def subset(m):
        values = re.findall(r"'([^']*)'", m[2])
        return 'dbo.JsonSubset(' + m[1] + ", N'" + json.dumps(values).replace("'", "''") + "')=1"
    value = re.sub(r'(\w+) <@ ARRAY\[(.*?)\]', subset, value)
    if c['name'] == 'ticket_prefix_registry_check':
        value = "((owner_kind='LEGACY' AND owner_id IS NULL) OR (owner_kind<>'LEGACY' AND owner_id IS NOT NULL))"
    assert not any(x in value for x in ['ANY ', 'ALL ', 'ARRAY[', '::', '<@', ' ~ ']), value
    pieces = re.split(r"('(?:[^']|'')*')", value)
    names = sorted((column['column_name'] for column in tables[c['table_name']]), key=len, reverse=True)
    for i in range(0, len(pieces), 2):
        for name in names:
            pieces[i] = re.sub(r'(?<!\[)\b' + re.escape(name) + r'\b(?!\])', q(name), pieces[i])
    return ''.join(pieces)

for c in constraints:
    if c['kind'] == 'c':
        output.append('ALTER TABLE dbo.' + q(c['table_name']) + ' ADD CONSTRAINT ' + q(c['name']) + ' CHECK (' + check(c) + ');\nGO\n')
    if c['kind'] == 'u':
        names = [n.strip() for n in re.search(r'\((.*?)\)', c['definition'])[1].split(',')]
        nullable = [n for n in names if next(col for col in tables[c['table_name']] if col['column_name'] == n)['is_nullable'] == 'YES' and next(col for col in tables[c['table_name']] if col['column_name'] == n)['is_generated'] != 'ALWAYS']
        statement = 'CREATE UNIQUE INDEX ' + q(c['name']) + ' ON dbo.' + q(c['table_name']) + '(' + ','.join(q(n) for n in names) + ')'
        if nullable and not ('id' in names and next(col for col in tables[c['table_name']] if col['column_name']=='id')['is_nullable']=='NO'):
            statement += ' WHERE ' + ' AND '.join(q(n) + ' IS NOT NULL' for n in nullable)
        output.append(statement + ';\nGO\n')

for c in constraints:
    if c['kind'] != 'f':
        continue
    match = re.fullmatch(r'FOREIGN KEY \(([^)]+)\) REFERENCES (?:public\.)?(\w+)\(([^)]+)\)(.*)', c['definition'])
    assert match and match[2] in tables, c
    source_names = ','.join(q(n.strip()) for n in match[1].split(','))
    target_names = ','.join(q(n.strip()) for n in match[3].split(','))
    output.append('ALTER TABLE dbo.' + q(c['table_name']) + ' ADD CONSTRAINT ' + q(c['name']) + ' FOREIGN KEY (' + source_names + ') REFERENCES dbo.' + q(match[2]) + '(' + target_names + ');\nGO\n')

# PostgreSQL expression/partial indexes and cascade behavior are tracked for review.
pending = {'indexes': [i for i in schema['indexes'] if i['table_name'] in tables and i['name'] not in {c['name'] for c in constraints}], 'foreignKeyActions': [c for c in constraints if c['kind']=='f' and re.search(r'ON DELETE|ON UPDATE', c['definition'])], 'nativeNumberDefaults': native_defaults}
(root / 'database').mkdir(exist_ok=True)
(root / 'database/001_schema.sql').write_text(''.join(output), encoding='utf-8')
(root / 'inventory/sql-schema-review.json').write_text(json.dumps(pending, indent=2), encoding='utf-8')
(root / 'inventory/sql-column-types.json').write_text(json.dumps({'types':column_types,'identityColumns':identity}, indent=2), encoding='utf-8')
print(json.dumps({'tables':len(tables),'columns':len(schema['columns']),'checks':sum(c['kind']=='c' for c in constraints),'foreignKeys':sum(c['kind']=='f' for c in constraints),'pendingIndexes':len(pending['indexes']),'pendingDeleteSemantics':len(pending['foreignKeyActions']),'schemaReadyForCutover':False}))
