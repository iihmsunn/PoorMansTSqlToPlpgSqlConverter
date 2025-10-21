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

using CommandLine;

namespace TSqlToPSql;

public class Options
{
    [Option('i', "input", Required = true, HelpText = "Input file")]
    public string? Input { get; set; }

    [Option('o', "output", Required = false, HelpText = "Output file, defaults to output.sql")]
    public string? Output { get; set; }

    [Option('t', "tree", Required = false, HelpText = "Original mssql syntax tree for debugging, defaults to debug/tree.txt")]
    public string? Tree { get; set; }

    [Option('p', "ptree", Required = false, HelpText = "Modified syntax tree for debugging, defaults to debug/tree.psql.txt")]
    public string? PTree { get; set; }

    [Option('D', "debug", Required = false, HelpText = "Debug mode")]
    public bool Debug { get; set; }
}
