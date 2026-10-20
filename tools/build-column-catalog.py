from pathlib import Path
import collections,json
root=Path(__file__).resolve().parents[1]
schema=json.loads((root/'inventory/schema.json').read_text(encoding='utf-8'))
tables=collections.defaultdict(list)
for c in schema['columns']: tables[c['table_name']].append(c['column_name'])
source=['// Generated from captured schema version 24 by tools/build-column-catalog.py.\nnamespace Astra.Api.Infrastructure;\npublic static class ColumnCatalog\n{\n private static readonly IReadOnlyDictionary<string,string[]> Catalog = new Dictionary<string,string[]>(StringComparer.Ordinal)\n {\n']
for table,columns in sorted(tables.items()):source.append('  ["'+table+'"] = ['+','.join('"'+c+'"' for c in columns)+'],\n')
source.append(' };\n public static IReadOnlyList<string> Columns(string table) => Catalog.TryGetValue(table,out var columns) ? columns : throw new ArgumentException("Unsupported table.",nameof(table));\n}\n')
(root/'src/Astra.Api/Infrastructure/ColumnCatalog.cs').write_text(''.join(source),encoding='utf-8')
print(json.dumps({'catalogTables':len(tables)}))
