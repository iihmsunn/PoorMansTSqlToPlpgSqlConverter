/*
Poor Man's T-SQL to PL/pgSQL Converter
Copyright (C) 2025 Oleg Mishunin

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using PoorMansTSqlFormatterLib.Tokenizers;
using PoorMansTSqlFormatterLib.Parsers;
using TSqlToPSql;
using CommandLine;

Options? options = null;
Parser.Default.ParseArguments<Options>(args).WithParsed<Options>(o => options = o);

if (options == null)
{
    Console.WriteLine("error: wrong arguments");
    return;
}

var sql = File.ReadAllText(options.Input);
var tokenizer = new TSqlStandardTokenizer();
var tokens = tokenizer.TokenizeSQL(sql, null);
var parser = new TSqlStandardParser();
var parsed = parser.ParseSQL(tokens);

var outputFile = options.Output ?? "output.sql";
var treeFile = options.Tree ?? "debug/tree.txt";
var pTreeFile = options.PTree ?? "debug/tree.psql.txt";

var dirs = new List<string> {
    Path.GetDirectoryName(outputFile)
};

if (options.Debug)
{
    dirs.AddRange(new List<string> {
      Path.GetDirectoryName(treeFile),
      Path.GetDirectoryName(pTreeFile)
    }); 
}

foreach (var dir in dirs.Where(e => !string.IsNullOrEmpty(e)))
{
    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
}

if (options.Debug)
{
    File.WriteAllText(treeFile, parsed.DumpTree());
}

var converterOptions = new TSqlToPsqlConverterOptions
{
    TrailingCommas = true,
    UppercaseKeywords = false
};

var converter = new TSqlToPsqlConverter(converterOptions);
var syntaxTreeTransformer = new SyntaxTreeTransformer();

syntaxTreeTransformer.TransformTree(parsed);

if (options.Debug) {
    File.WriteAllText(pTreeFile, parsed.DumpTree());
}

var output = converter.FormatSQLTree(parsed);

File.WriteAllText(outputFile, output);
