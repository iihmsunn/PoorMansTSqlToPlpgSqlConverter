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

using PoorMansTSqlFormatterLib.Interfaces;
using PoorMansTSqlFormatterLib.ParseStructure;

namespace TSqlToPSql;

public enum SelectColumnType {
    NameOnly,
    NameAndTableOnly,
    Aliased,
    Computed
}

public class SyntaxTreeTransformer {
    private int WarningCount = 0;

    private Dictionary<string, string> DirectlyMappedFunctions = new Dictionary<string, string> {
        { "len", "length" },
        { "getdate", "now" },
        { "rand", "random" },
        { "newid", "gen_random_uuid" },
        { "isnull", "coalesce" },
        { "scope_identity", "lastval" }
    };

    private Dictionary<string, string> DateMapping = new Dictionary<string, string> {
        { "mm", "MI" }
    };

    private Dictionary<string, string> DataTypeMapping = new Dictionary<string, string> {
        { "nvarchar", "text" }, 
        { "varchar", "text" },
        { "datetime", "timestamp" },
        { "datetimeoffset", "timestamptz" },
        { "bit", "boolean" },
        { "uniqueidentifier", "uuid" },
        { "decimal", "numeric" }
    };
    
    private void AddMissingSemicolons(Node element)
    {
        if (element.Name == SqlStructureConstants.ENAME_EXPRESSION_PARENS) return;
        if (element.Name == SqlStructureConstants.ENAME_FUNCTION_PARENS) return;
        if (element.Name == SqlStructureConstants.ENAME_SELECTIONTARGET_PARENS) return;

        if (element.Name == SqlStructureConstants.ENAME_SQL_CLAUSE && element.Parent.Children.Last() == element)
        {
            var containsWhileLoop = element.ChildByName(SqlStructureConstants.ENAME_WHILE_LOOP) != null;
            var containsTryBlock = element.ChildByName(SqlStructureConstants.ENAME_TRY_BLOCK) != null;

            var semicolon = element.ChildByName(SqlStructureConstants.ENAME_SEMICOLON);
            if (semicolon == null && !containsWhileLoop && !containsTryBlock)
            {
                var lastChild = element.Children.LastOrDefault();
                if (lastChild == null) return;

                var isEndFollowedByElse = false;
                if (element.ChildByName(SqlStructureConstants.ENAME_BEGIN_END_BLOCK) != null)
                {
                    var ifStatement = element.Parent?.Parent?.Parent?.Name == SqlStructureConstants.ENAME_IF_STATEMENT ? element.Parent.Parent.Parent : null;
                    var elseClause = ifStatement?.ChildByName(SqlStructureConstants.ENAME_ELSE_CLAUSE);
                    if (elseClause != null)
                    {
                        isEndFollowedByElse = true;
                    }
                }

                if (
                    lastChild.Name != SqlStructureConstants.ENAME_DDL_PROCEDURAL_BLOCK
                    && lastChild.Name != SqlStructureConstants.ENAME_IF_STATEMENT
                    && !element.EndsWithSemicolon()
                    && !isEndFollowedByElse)
                {
                    var endsWithWhitespace = lastChild.Name == SqlStructureConstants.ENAME_WHITESPACE;

                    if (endsWithWhitespace)
                    {
                        element.InsertChildBefore(SqlStructureConstants.ENAME_SEMICOLON, ";", lastChild);
                    }
                    else
                    {
                        element.AddChild(SqlStructureConstants.ENAME_SEMICOLON, ";");
                    }
                }
            }
        }

        foreach (var child in element.Children)
        {
            AddMissingSemicolons(child);
        }
    }

    private void AddLanguageClause(Node element)
    {
        if (element.Name == SqlStructureConstants.ENAME_DDL_PROCEDURAL_BLOCK)
        {
            var asBlock = element.ChildByName(SqlStructureConstants.ENAME_DDL_AS_BLOCK);
            var temp = element.InsertChildBefore(SqlStructureConstants.ENAME_OTHERKEYWORD, "language", asBlock);
            temp = element.InsertChildAfter(SqlStructureConstants.ENAME_STRING, "plpgsql", temp);

            return;
        }

        foreach (var child in element.Children)
        {
            AddLanguageClause(child);
        }
    }

    private void WrapInBeginEndBlock(Node container)
    {
        var hasBeginEnd = container.Children.Any(st => st.Children.Any(cl => cl.Children.Any(e => e.Name == SqlStructureConstants.ENAME_BEGIN_END_BLOCK)));
        if (hasBeginEnd) return;

        var beginEndStatement = new NodeImpl
        {
            Name = SqlStructureConstants.ENAME_SQL_STATEMENT,
            TextValue = ""
        };

        var beginEndClause = beginEndStatement.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
        var beginEndBlock = beginEndClause.AddChild(SqlStructureConstants.ENAME_BEGIN_END_BLOCK, "");
        var beginEndBlockOpener = beginEndBlock.AddChild(SqlStructureConstants.ENAME_CONTAINER_OPEN, "");
        beginEndBlockOpener.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "begin");
        var beginEndBody = beginEndBlock.AddChild(SqlStructureConstants.ENAME_CONTAINER_MULTISTATEMENT, "");

        var statements = new List<Node>(container.Children);

        foreach (var statement in statements)
        {
            container.RemoveChild(statement);
            beginEndBody.AddChild(statement);
        }

        var beginEndBlockCloser = beginEndBlock.AddChild(SqlStructureConstants.ENAME_CONTAINER_CLOSE, "");
        beginEndBlockCloser.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "end");

        container.AddChild(beginEndStatement);
    }

    private Node WrapInExpressionParens(Node container) {
        var keyword = container.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD)!;
        var index = container.Children.ToList().IndexOf(keyword);
        var content = container.Children.Skip(index + 1).Where(e => e.Name != SqlStructureConstants.ENAME_SEMICOLON).ToList();
        var parens = container.InsertChildAfter(SqlStructureConstants.ENAME_EXPRESSION_PARENS, "", keyword);
        foreach (var child in content) {
            container.RemoveChild(child);
            parens.AddChild(child);
        }

        return parens;
    }

    private void ForceDdlBeginEnd(Node element)
    {
        if (element.Name == SqlStructureConstants.ENAME_DDL_AS_BLOCK)
        {
            var ddlBody = element.ChildByName(SqlStructureConstants.ENAME_CONTAINER_GENERALCONTENT);
            WrapInBeginEndBlock(ddlBody);

            return;
        }

        foreach (var child in element.Children)
        {
            ForceDdlBeginEnd(child);
        }
    }

    private void ForceDdlParens(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "procedure")) {
            var clauseNodes = element.Parent.Children.ToList();
            var schema = element.NextNonWsSibling();
            var period = schema.NextNonWsSibling();
            var procedureName = period.Matches(SqlStructureConstants.ENAME_PERIOD) ? period.NextNonWsSibling() : schema;
            var ddlAsBlock = element.Parent.ChildByName(SqlStructureConstants.ENAME_DDL_AS_BLOCK);
            var nameIndex = clauseNodes.IndexOf(procedureName);
            var asBlockIndex = clauseNodes.IndexOf(ddlAsBlock);
            var parameters = clauseNodes.Skip(nameIndex + 1).Take(asBlockIndex - nameIndex - 1);

            var ddlParens = element.Parent.InsertChildBefore(SqlStructureConstants.ENAME_DDL_PARENS, "", ddlAsBlock);
            foreach (var node in parameters) {
                node.Parent.RemoveChild(node);
                ddlParens.AddChild(node);
            }
        }

        foreach (var child in element.Children.ToList())
        {
            ForceDdlParens(child);
        }
    }

    private void AddBlockWrapper(Node element)
    {
        if (element.Name == SqlStructureConstants.ENAME_DDL_AS_BLOCK)
        {
            var containerOpener = element.ChildByName(SqlStructureConstants.ENAME_CONTAINER_OPEN);
            containerOpener.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "$$");

            var containerCloser = element.AddChild(SqlStructureConstants.ENAME_CONTAINER_CLOSE, "");
            containerCloser.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "$$");
            containerCloser.AddChild(SqlStructureConstants.ENAME_SEMICOLON, ";");
            return;
        }

        foreach (var child in element.Children)
        {
            AddBlockWrapper(child);
        }
    }

    private List<Node> TrimNodeList(List<Node>? list)
    {
        if (list?.FirstOrDefault()?.Name == SqlStructureConstants.ENAME_WHITESPACE) {
            list.RemoveAt(0);
        }
    
        if (list?.LastOrDefault()?.Name == SqlStructureConstants.ENAME_WHITESPACE)
        {
            list.RemoveAt(list.Count - 1);
        }

        return list ?? [];
    }

    private List<(List<Node> variable, List<Node> value)> GetDeclarationsFromDeclareBlock(Node element)
    {
        List<(List<Node> variable, List<Node> value)> result = [];
        var body = element.ChildByName(SqlStructureConstants.ENAME_CONTAINER_GENERALCONTENT);
        var flatNodeList = element.Children.Concat(body?.Children ?? []).Where(e => e != body).ToList();
        List<Node>? currentVariable = null;
        List<Node>? currentValue = null;
        bool inValue = false;

        foreach (var node in flatNodeList)
        {
            if (!inValue)
                switch (node.Name)
                {
                    case SqlStructureConstants.ENAME_OTHERNODE:
                    case SqlStructureConstants.ENAME_BRACKET_QUOTED_NAME:
                        if (node.NextSibling().Matches(SqlStructureConstants.ENAME_PERIOD)) {
                            //if table type schema - do nothing
                        }
                        else if (node.PreviousSibling().Matches(SqlStructureConstants.ENAME_PERIOD)) {
                            //if table type name - add as array type
                            currentVariable?.Add(node);
                            currentVariable?.Add(new NodeImpl { Name = SqlStructureConstants.ENAME_PERIOD, TextValue = "[]" });
                        }
                        else
                        {
                            //other name - new variable
                            if (TrimNodeList(currentVariable)?.Any() ?? false)
                            {
                                result.Add((TrimNodeList(currentVariable), TrimNodeList(currentValue)));
                            }
                            inValue = false;
                            currentVariable = new List<Node> { node };
                        }
                        break;
                    case SqlStructureConstants.ENAME_WHITESPACE:
                        if (currentVariable?.LastOrDefault()?.Name != SqlStructureConstants.ENAME_WHITESPACE)
                        {
                            currentVariable?.Add(node);
                        }

                        break;
                    case SqlStructureConstants.ENAME_DATATYPE_KEYWORD:
                    case SqlStructureConstants.ENAME_DDLDETAIL_PARENS:
                        currentVariable?.Add(node);
                        break;
                    case SqlStructureConstants.ENAME_EQUALSSIGN:
                        inValue = true;
                        currentValue = new List<Node>();
                        break;
                    case SqlStructureConstants.ENAME_COMMENT_MULTILINE:
                    case SqlStructureConstants.ENAME_COMMENT_SINGLELINE:
                        currentVariable?.Add(node);
                        break;
                    default:
                        break;
                }
            else switch (node.Name)
                {
                    case SqlStructureConstants.ENAME_COMMA:
                        inValue = false;
                        if (TrimNodeList(currentVariable)?.Any() ?? false)
                        {
                            result.Add((TrimNodeList(currentVariable), TrimNodeList(currentValue)));
                            currentVariable = [];
                            currentValue = [];
                        }
                        break;
                    default:
                        currentValue?.Add(node);
                        break;
                }
        }

        if (currentVariable?.Any() ?? false)
        {
            result.Add((TrimNodeList(currentVariable), TrimNodeList(currentValue)));
        }

        return result;
    }

    private List<(List<Node> variable, List<Node> value)> FindDeclarations(Node element)
    {
        List<(List<Node> variable, List<Node> value)> result = [];

        if (element.Name == SqlStructureConstants.ENAME_DDL_DECLARE_BLOCK)
        {
            return GetDeclarationsFromDeclareBlock(element);
        }

        foreach (var child in element.Children)
        {
            result.AddRange(FindDeclarations(child));
        }

        return result;
    }

    private void AddDeclareSection(Node element, List<(List<Node> variable, List<Node> value)> declarations)
    {
        if (element.Name == SqlStructureConstants.ENAME_DDL_AS_BLOCK)
        {
            if (declarations.Count == 0) return;

            var statement = element
                .ChildByName(SqlStructureConstants.ENAME_CONTAINER_GENERALCONTENT)
                .ChildByName(SqlStructureConstants.ENAME_SQL_STATEMENT);

            var beginEndBlock = statement
                .ChildByName(SqlStructureConstants.ENAME_SQL_CLAUSE);

            var wrapper = statement.InsertChildBefore(SqlStructureConstants.ENAME_SQL_CLAUSE, "", beginEndBlock);

            var header = wrapper.AddChild(SqlStructureConstants.ENAME_DDL_DECLARE_BLOCK, "");
            header.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "declare");

            wrapper.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");

            foreach (var declaration in declarations)
            {
                var type = declaration.variable.FirstOrDefault(e => e.Matches(SqlStructureConstants.ENAME_DATATYPE_KEYWORD));
                var otherNames = declaration.variable.FindAll(e => e.IsName());

                //skip if datatype unknown
                if (type == null && otherNames.Count < 2) continue;
            
                var block = wrapper.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
                foreach (var node in declaration.variable.FindAll(e => !e.IsComment()))
                {
                    block.AddChild((Node)node.Clone());
                }

                block.AddChild(SqlStructureConstants.ENAME_SEMICOLON, ";");
                var comments = declaration.value.FindAll(e => e.IsComment());
                comments.AddRange(declaration.variable.FindAll(e => e.IsComment()));
                foreach (var node in comments)
                {
                    block.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, " ");
                    block.AddChild((Node)node.Clone());
                }
            }

            return;
        }

        foreach (var child in element.Children)
        {
            AddDeclareSection(child, declarations);
        }
    }

    private static string[] PSqlUnnecessaryKeywords = ["lineno", "nocount"];

    private void RemoveUnnecessaryStatements(Node element)
    {
        if (element.Name == SqlStructureConstants.ENAME_OTHERKEYWORD && PSqlUnnecessaryKeywords.Contains(element.TextValue))
        {
            var statement = element.Parent.Parent;
            statement.Parent.RemoveChild(statement);
        }

        //sometimes it's necessary for mssql to have statements containing nothing but semicolon, those are removed
        if (element.Matches(SqlStructureConstants.ENAME_SQL_CLAUSE) && element.Parent.Children.Count() == 1 && element.Children.First().Matches(SqlStructureConstants.ENAME_SEMICOLON)) {
            var statement = element.Parent;
            statement.Parent.RemoveChild(statement);
        }

        foreach (var child in new List<Node>(element.Children))
        {
            RemoveUnnecessaryStatements(child);
        }
    }

    private void ConvertDeclareToAssign(Node element)
    {
        if (element.Name == SqlStructureConstants.ENAME_DDL_DECLARE_BLOCK)
        {
            var declarations = GetDeclarationsFromDeclareBlock(element);
            var assignments = declarations.FindAll(e => e.value?.Count > 0);
            if (assignments.Count == 0) return;

            Node? nextClause = null;
            var container = element.Parent.Parent;
            foreach (var assignment in assignments)
            {
                var name = assignment.variable.First(e => e.IsName());
                var value = assignment.value.Where(e =>
                    e.Name != SqlStructureConstants.ENAME_WHITESPACE
                    && !e.IsComment()
                    && e.Name != SqlStructureConstants.ENAME_SEMICOLON
                );

                nextClause = container.InsertChildAfter(SqlStructureConstants.ENAME_SQL_CLAUSE, "", nextClause ?? element.Parent);

                name.Parent.RemoveChild(name);

                foreach (var node in value)
                {
                    node.Parent.RemoveChild(node);
                }
                nextClause.AddChild(name);
                nextClause.AddChild(SqlStructureConstants.ENAME_EQUALSSIGN, ":=");
                foreach (var node in value)
                {
                    nextClause.AddChild(node);
                }
                nextClause.AddChild(SqlStructureConstants.ENAME_SEMICOLON, ";");
            }

            element.Parent.Parent.RemoveChild(element.Parent);

            return;
        }

        foreach (var child in new List<Node>(element.Children))
        {
            ConvertDeclareToAssign(child);
        }
    }

    private void CleanupDeclareStatements(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_DDL_DECLARE_BLOCK)) {
            var statement = element.Closest(SqlStructureConstants.ENAME_SQL_STATEMENT)!;

            if (statement.Parent.Matches(SqlStructureConstants.ENAME_CONTAINER_GENERALCONTENT)) return;
            
            statement.Parent.RemoveChild(statement);
            return;
        }

        foreach (var child in new List<Node>(element.Children)) {
            CleanupDeclareStatements(child);
        }
    }

    private void ConvertSetToAssign(Node element)
    {    
        if (element.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "set"))
        {
            var clause = element.Parent;
            var statement = clause.Parent;

            //skip if part of update statement
            var updateClause = statement.Children.FirstOrDefault(e => e.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "update") != null);
            if (updateClause != null) return;
            
            var equalsSign = clause.ChildByName(SqlStructureConstants.ENAME_EQUALSSIGN);
            if (equalsSign == null) return;

            clause.RemoveChild(element);
            equalsSign.TextValue = ":=";
            return;
        }

        foreach (var child in new List<Node>(element.Children))
        {
            ConvertSetToAssign(child);
        }
    }

    private void ForceIfBeginEnd(Node element)
    {
        if (element.Name == SqlStructureConstants.ENAME_IF_STATEMENT)
        {
            var container = element.ChildByName(SqlStructureConstants.ENAME_CONTAINER_SINGLESTATEMENT);
            WrapInBeginEndBlock(container);
        }
        else if (element.Name == SqlStructureConstants.ENAME_ELSE_CLAUSE)
        {
            var container = element.ChildByName(SqlStructureConstants.ENAME_CONTAINER_SINGLESTATEMENT);
            var nestedIf = container
                .ChildByName(SqlStructureConstants.ENAME_SQL_STATEMENT)
                .ChildByName(SqlStructureConstants.ENAME_SQL_CLAUSE)
                .ChildByName(SqlStructureConstants.ENAME_IF_STATEMENT);

            if (nestedIf == null)
            {
                WrapInBeginEndBlock(container);
            }
        }

        foreach (var child in new List<Node>(element.Children))
        {
            ForceIfBeginEnd(child);
        }
    }

    private void ConvertConditions(Node element)
    {
        if (element.Name == SqlStructureConstants.ENAME_IF_STATEMENT
            || element.Name == SqlStructureConstants.ENAME_ELSE_CLAUSE
        )
        {
            var beginEndBlock = element
                .ChildByName(SqlStructureConstants.ENAME_CONTAINER_SINGLESTATEMENT)
                .ChildByName(SqlStructureConstants.ENAME_SQL_STATEMENT)
                .ChildByName(SqlStructureConstants.ENAME_SQL_CLAUSE)
                .ChildByName(SqlStructureConstants.ENAME_BEGIN_END_BLOCK);

            if (beginEndBlock != null)
            {
                var begin = beginEndBlock
                .ChildByName(SqlStructureConstants.ENAME_CONTAINER_OPEN)
                .Children.First();

                if (element.Name == SqlStructureConstants.ENAME_ELSE_CLAUSE)
                {
                    begin.Parent.Parent.RemoveChild(begin.Parent);
                }
                else
                {
                    begin.TextValue = "then";
                }

                var end = beginEndBlock
                    .ChildByName(SqlStructureConstants.ENAME_CONTAINER_CLOSE)
                    .Children.First();

                var elseClause = element.ChildByName(SqlStructureConstants.ENAME_ELSE_CLAUSE);
                if (elseClause == null)
                {
                    end.TextValue = "end if";
                }
                else
                {
                    end.Parent.Parent.RemoveChild(end.Parent);
                }
            }
            else
            {
                var elseIf = element
                    .ChildByName(SqlStructureConstants.ENAME_CONTAINER_OPEN)
                    .ChildByName(SqlStructureConstants.ENAME_OTHERKEYWORD);
                elseIf.TextValue = "elsif";

                var nestedIf = element
                    .ChildByName(SqlStructureConstants.ENAME_CONTAINER_SINGLESTATEMENT)
                    .ChildByName(SqlStructureConstants.ENAME_SQL_STATEMENT)
                    .ChildByName(SqlStructureConstants.ENAME_SQL_CLAUSE)
                    .ChildByName(SqlStructureConstants.ENAME_IF_STATEMENT);

                var nestedIfOpener = nestedIf.ChildByName(SqlStructureConstants.ENAME_CONTAINER_OPEN);
                nestedIf.RemoveChild(nestedIfOpener);
            }
        }

        foreach (var child in new List<Node>(element.Children))
        {
            ConvertConditions(child);
        }
    }

    private List<List<Node>> GetSelectColumns(Node selectClause)
    {
        List<List<Node>> columns = [];
        List<Node> currentColumn = [];
        var selectKeyword = selectClause.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "select");

        foreach (var node in selectClause.Children.Where(e => e != selectKeyword))
        {
            switch (node.Name)
            {
                case SqlStructureConstants.ENAME_COMMA:
                    columns.Add(currentColumn);
                    currentColumn = [];
                    break;
                default:
                    currentColumn.Add(node);
                    break;
            }
        }

        columns.Add(currentColumn);

        return columns;
    }

    private void ConvertSelectEqualsSignToAs(Node selectClause)
    {
        var columns = GetSelectColumns(selectClause);

        foreach (var column in columns)
        {
            var equalsSign = column.FirstOrDefault(e => e.Name == SqlStructureConstants.ENAME_EQUALSSIGN);
            if (equalsSign == null) continue;

            var splitIndex = column.IndexOf(equalsSign);
            var name = column.First(e => e.IsName());
            var value = column.Skip(splitIndex + 1).Where(e => !e.IsComment());
            selectClause.RemoveChild(name);

            foreach (var node in value)
            {
                selectClause.RemoveChild(node);
                selectClause.InsertChildBefore(node, equalsSign);
            }

            selectClause.InsertChildAfter(name, equalsSign);
            equalsSign.TextValue = "as";
            equalsSign.Name = SqlStructureConstants.ENAME_OTHERKEYWORD;
        }
    }

    private void AddSelectIntoClause(Node selectClause)
    {
        var semicolon = selectClause.ChildByName(SqlStructureConstants.ENAME_SEMICOLON);
        if (semicolon != null) {
            selectClause.RemoveChild(semicolon);
        }
        
        var columns = GetSelectColumns(selectClause);
        var statement = selectClause.Parent;

        var relevantColumns = columns.FindAll(e =>
            e.FirstOrDefault(e => e.Name == SqlStructureConstants.ENAME_EQUALSSIGN) != null
            && e.First(e => e.IsName()).TextValue.StartsWith("@")
        );

        if (relevantColumns.Count == 0) return;

        var intoClause = statement.InsertChildAfter(SqlStructureConstants.ENAME_SQL_CLAUSE, "", selectClause);
        intoClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "into");

        var lastColumn = relevantColumns.Last();
        foreach (var column in relevantColumns)
        {
            var name = column.First(e => e.IsName());
            intoClause.AddChild(SqlStructureConstants.ENAME_OTHERNODE, name.TextValue);
            if (column != lastColumn)
            {
                intoClause.AddChild(SqlStructureConstants.ENAME_COMMA, ",");
            }
        }
    }

    private void ConvertSelectTopToLimit(Node selectClause)
    {
        var statement = selectClause.Parent;

        var top = selectClause.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "top");
        if (top == null) return;

        var topValue = top.NextNonWsSibling();

        selectClause.RemoveChild(top);
        selectClause.RemoveChild(topValue);

        var lastClause = statement.Children.Last(e => e.Name == SqlStructureConstants.ENAME_SQL_CLAUSE);
        var semicolon = lastClause.FindSemicolon();
        if (semicolon != null)
        {
            semicolon.Parent.RemoveChild(semicolon);
        }

        var limitClause = statement.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
        limitClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "limit");
        limitClause.AddChild(topValue);
    }

    private void ConvertSelectIntoClause(Node selectClause)
    {
        var statement = selectClause.Parent;
        var intoClause = selectClause.NextSibling();
        if (intoClause?.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "into") == null)
        {
            return;
        }

        var tableName = intoClause.Children.First(e => e.IsName()).TextValue;
        statement.RemoveChild(intoClause);
        var createTableClause = statement.InsertChildBefore(SqlStructureConstants.ENAME_SQL_CLAUSE, "", selectClause);
        createTableClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "create");
        createTableClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "temp");
        createTableClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "table");
        createTableClause.AddChild(SqlStructureConstants.ENAME_OTHERNODE, tableName);
        createTableClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "as");
    }

    private void UpdateSelectStatements(Node element)
    {
        if (element.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "select"))
        {
            var selectClause = element.Parent;
            ConvertSelectIntoClause(selectClause);
            AddSelectIntoClause(selectClause);
            ConvertSelectEqualsSignToAs(selectClause);
            ConvertSelectTopToLimit(selectClause);
        }

        foreach (var child in new List<Node>(element.Children))
        {
            UpdateSelectStatements(child);
        }
    }

    private Dictionary<string, Node> ConvertTempTables(Node element, Dictionary<string, Node>? tableDefinitions = null)
    {
        if (tableDefinitions == null) tableDefinitions = new();
    
        if (element.Name == SqlStructureConstants.ENAME_DDL_OTHER_BLOCK)
        {
            var create = element.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "create");
            var table = element.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "table");

            if (create == null || table == null) return tableDefinitions;
            var name = element.ChildByName(SqlStructureConstants.ENAME_OTHERNODE);

            if (!name.TextValue.StartsWith("#")) return tableDefinitions;

            element.InsertChildAfter(SqlStructureConstants.ENAME_OTHERKEYWORD, "temp", create);

            var definition = name.NextNonWsSibling();
            tableDefinitions[name.TextValue.ToLower()] = definition;

            return tableDefinitions;
        }

        foreach (var child in new List<Node>(element.Children))
        {
            ConvertTempTables(child, tableDefinitions);
        }

        return tableDefinitions;
    }

    private void ConvertOldDropTableIfExists(Node element)
    {
        if (element.Matches(SqlStructureConstants.ENAME_IF_STATEMENT))
        {
            var condition = element
                .ChildByName(SqlStructureConstants.ENAME_BOOLEAN_EXPRESSION)
                .ChildByNameAndText(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "object_id");

            var drop = element
                .ChildByName(SqlStructureConstants.ENAME_CONTAINER_SINGLESTATEMENT)
                .ChildByName(SqlStructureConstants.ENAME_SQL_STATEMENT)
                .ChildByName(SqlStructureConstants.ENAME_SQL_CLAUSE)
                .ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "drop");

            if (condition == null || drop == null) return;

            var dropClause = drop.Parent;
            var tableName = dropClause.ChildByName(SqlStructureConstants.ENAME_OTHERNODE);
            dropClause.InsertChildBefore(SqlStructureConstants.ENAME_OTHERKEYWORD, "if", tableName);
            dropClause.InsertChildBefore(SqlStructureConstants.ENAME_OTHERKEYWORD, "exists", tableName);

            var ifStatement = element.Parent.Parent;
            var dropStatement = dropClause.Parent;
            var container = ifStatement.Parent;
            dropStatement.Parent.RemoveChild(dropStatement);
            container.InsertChildAfter(dropStatement, ifStatement);
            container.RemoveChild(ifStatement);

            return;
        }

        foreach (var child in new List<Node>(element.Children))
        {
            ConvertOldDropTableIfExists(child);
        }
    }

    private void ConvertLoops(Node element)
    {
        if (element.Matches(SqlStructureConstants.ENAME_WHILE_LOOP))
        {
            var beginEndBlock = element
                .ChildByName(SqlStructureConstants.ENAME_CONTAINER_SINGLESTATEMENT)
                .ChildByName(SqlStructureConstants.ENAME_SQL_STATEMENT)
                .ChildByName(SqlStructureConstants.ENAME_SQL_CLAUSE)
                .ChildByName(SqlStructureConstants.ENAME_BEGIN_END_BLOCK);

            var begin = beginEndBlock
                .ChildByName(SqlStructureConstants.ENAME_CONTAINER_OPEN)
                .ChildByName(SqlStructureConstants.ENAME_OTHERKEYWORD);

            var end = beginEndBlock
                .ChildByName(SqlStructureConstants.ENAME_CONTAINER_CLOSE)
                .ChildByName(SqlStructureConstants.ENAME_OTHERKEYWORD);

            begin.TextValue = "loop";
            end.TextValue = "end loop";
        }

        foreach (var child in new List<Node>(element.Children))
        {
            ConvertLoops(child);
        }
    }

    private void ConvertProceduralBlocks(Node element)
    {
        if (element.Matches(SqlStructureConstants.ENAME_DDL_PROCEDURAL_BLOCK))
        {
            var alter = element.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "alter");
            if (alter != null) alter.TextValue = "replace";

            //if function returns a table variable, it has to be created as temp table
            var returns = element.ChildByName(SqlStructureConstants.ENAME_DDL_RETURNS);
            var tableName = returns?.NextNonWsSibling();
            var tableKeyword = tableName?.NextNonWsSibling();
            if (tableKeyword?.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "table") ?? false) {
                var tableParens = tableKeyword.NextNonWsSibling();
                var functionBody = element
                    .ChildByName(SqlStructureConstants.ENAME_DDL_AS_BLOCK)
                    .ChildByName(SqlStructureConstants.ENAME_CONTAINER_GENERALCONTENT)
                    .ChildByName(SqlStructureConstants.ENAME_SQL_STATEMENT)
                    .ChildByName(SqlStructureConstants.ENAME_SQL_CLAUSE)
                    .ChildByName(SqlStructureConstants.ENAME_BEGIN_END_BLOCK)
                    .ChildByName(SqlStructureConstants.ENAME_CONTAINER_MULTISTATEMENT);

                var createTableStatement = functionBody.InsertChildBefore(SqlStructureConstants.ENAME_SQL_STATEMENT, "", functionBody.Children.First());
                var clause = createTableStatement.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
                clause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "create");
                clause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "temp");
                clause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "table");
                tableName!.Parent.RemoveChild(tableName);
                clause.AddChild(tableName);
                clause.AddChild((Node)tableParens.Clone());

                var returnStatement = functionBody.Children.First(s => s.Children.First().ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "return") != null);
                var returnClause = returnStatement.Children.First();
                var returnKeyword = returnClause.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "return");
                var varName = returnKeyword!.NextNonWsSibling();
                if (varName != null) {
                    returnClause.RemoveChild(varName);
                }

                returnClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "query");
                var returnSelectClause = returnStatement.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
                returnSelectClause.AddChild(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "select");
                returnSelectClause.AddChild(SqlStructureConstants.ENAME_ASTERISK, "*");
                var returnFromClause = returnStatement.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
                returnFromClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "from");
                var selectionTarget = returnFromClause.AddChild(SqlStructureConstants.ENAME_SELECTIONTARGET, "");
                selectionTarget.AddChild((Node)tableName.Clone());
            }
            
            return;
        }

        foreach (var child in element.Children)
        {
            ConvertProceduralBlocks(child);
        }
    }

    private void UpdateNames(Node element)
    {
        if (element.IsName())
        {
            element.TextValue = element.TextValue.ToSnakeCase();
            element.TextValue = element.TextValue.Replace("@", "_").Replace("#", "_");
        }

        foreach (var child in element.Children)
        {
            UpdateNames(child);
        }
    }

    private void FixCommasAfterComments(Node element)
    {
        if (element.IsComment())
        {
            var clause = element.Parent;
            var comma = element.NextNonWsSibling(true);
            if (!(comma?.Matches(SqlStructureConstants.ENAME_COMMA) ?? false))
            {
                return;
            }

            element.Parent.RemoveChild(comma);
            element.Parent.InsertChildBefore(comma, element);
        }

        foreach (var child in new List<Node>(element.Children))
        {
            FixCommasAfterComments(child);
        }
    }

    private void ConvertCast(Node element)
    {
        if (element.Matches(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "cast"))
        {
            var parens = element.NextSibling();
            var asKeyword = parens.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "as")!;
            var type = asKeyword.NextNonWsSibling();
            var typeParens = parens.ChildByName(SqlStructureConstants.ENAME_DDLDETAIL_PARENS);
            var value = parens.Children.First(e => e.Name != SqlStructureConstants.ENAME_WHITESPACE);
            var valueParens = parens.ChildByName(SqlStructureConstants.ENAME_FUNCTION_PARENS);
            var clause = element.Parent;
            parens.RemoveChild(value);

            clause.InsertChildBefore(value, element);
            if (valueParens != null) {
                clause.RemoveChild(valueParens);
                clause.InsertChildAfter(valueParens, value);
                value = valueParens;
            }
            clause.RemoveChild(element);
            clause.RemoveChild(parens);

            var temp = clause.InsertChildAfter(SqlStructureConstants.ENAME_PERIOD, "::", value);
            clause.InsertChildAfter(type, temp);
            if (typeParens != null)
            {
                clause.InsertChildAfter(typeParens, type);    
            }
        }

        foreach (var child in new List<Node>(element.Children))
        {
            ConvertCast(child);
        }
    }

    private void ConvertTryCast(Node element)
    {
        if (element.Matches(SqlStructureConstants.ENAME_OTHERNODE, "try_cast"))
        {
            var parens = element.NextSibling();
            var type = parens.ChildByName(SqlStructureConstants.ENAME_DATATYPE_KEYWORD);
            var typeParens = parens.ChildByName(SqlStructureConstants.ENAME_DDLDETAIL_PARENS);
            var value = parens.Children.First(e => e.Name != SqlStructureConstants.ENAME_WHITESPACE);
            var _as = parens.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "as")!;
            parens.RemoveChild(_as);
            parens.InsertChildAfter(SqlStructureConstants.ENAME_COMMA, ",", value);
            parens.InsertChildBefore(SqlStructureConstants.ENAME_OTHERKEYWORD, "null", type);
            parens.InsertChildBefore(SqlStructureConstants.ENAME_PERIOD, "::", type);
        }

        foreach (var child in new List<Node>(element.Children))
        {
            ConvertTryCast(child);
        }
    }

    private void ConvertUpdateFrom(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "update")) {
            var updateClause = element.Parent;
            var updateStatement = updateClause.Parent;

            var fromClause = updateStatement.Children.FirstOrDefault(e => e.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "from") != null);
            if (fromClause == null) return;
            
            var target = updateClause.ChildByName(SqlStructureConstants.ENAME_OTHERNODE);
            var joinClauses = updateStatement.Children.Where(e => e.ChildByName(SqlStructureConstants.ENAME_COMPOUNDKEYWORD) != null).ToList();

            var selectionTargets = new List<Node>() { fromClause.ChildByName(SqlStructureConstants.ENAME_SELECTIONTARGET) };
            selectionTargets.AddRange(joinClauses.Select(e => e.ChildByName(SqlStructureConstants.ENAME_SELECTIONTARGET)));

            var matchingSelectionTarget = selectionTargets.FirstOrDefault(e => e.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERNODE, target.TextValue) != null);
            if (matchingSelectionTarget == null) return;

            var selectionTarget = matchingSelectionTarget.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERNODE, target.TextValue)!;
            var isAlias = matchingSelectionTarget.Children.ToList().IndexOf(selectionTarget) > 0;

            var selectionTargetTable = !isAlias ? selectionTarget : matchingSelectionTarget.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERNODE)!;
            var selectionTargetAlias = selectionTargetTable.NextNonWsSibling();

            if (isAlias) {
                target.TextValue = selectionTargetTable.TextValue;
            }

            updateClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "as");
            updateClause.AddChild(SqlStructureConstants.ENAME_OTHERNODE, "__target__");

            var whereClause = updateStatement.Children.First(e => e.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "where") != null);
            var temp = WrapInExpressionParens(whereClause);
            temp = whereClause.InsertChildAfter(SqlStructureConstants.ENAME_AND_OPERATOR, "", temp);
            temp.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "and");
            temp = whereClause.InsertChildAfter(SqlStructureConstants.ENAME_OTHERNODE, "__target__", temp);
            temp = whereClause.InsertChildAfter(SqlStructureConstants.ENAME_PERIOD, ".", temp);
            temp = whereClause.InsertChildAfter(SqlStructureConstants.ENAME_OTHERNODE, "ctid", temp);
            temp = whereClause.InsertChildAfter(SqlStructureConstants.ENAME_EQUALSSIGN, "=", temp);
            temp = whereClause.InsertChildAfter(SqlStructureConstants.ENAME_OTHERNODE, selectionTargetAlias?.TextValue ?? selectionTarget.TextValue, temp);
            temp = whereClause.InsertChildAfter(SqlStructureConstants.ENAME_PERIOD, ".", temp);
            temp = whereClause.InsertChildAfter(SqlStructureConstants.ENAME_OTHERNODE, "ctid", temp);

            var setClause = updateStatement.Children.First(e => e.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "set") != null);
            return;
        }

        foreach (var child in new List<Node>(element.Children)) {
            ConvertUpdateFrom(child);
        }
    }

    private void ConvertJsonFunctions(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_OTHERNODE, "json_query"))
        {
            var parens = element.NextNonWsSibling();
            var comma = parens.ChildByName(SqlStructureConstants.ENAME_COMMA);
            if (comma != null) {
                //if comma exists, there are two arguments, and function already works
            } else {
                //if there is one argument, json_query is more like cast to json
                element.Name = SqlStructureConstants.ENAME_FUNCTION_KEYWORD;
                element.TextValue = "cast";
                parens.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "as");
                parens.AddChild(SqlStructureConstants.ENAME_DATATYPE_KEYWORD, "json");
            }
        }

        if (element.Matches(SqlStructureConstants.ENAME_OTHERNODE, "isjson"))
        {
            // isjson(@a) is replaced with (@a is json), always in expression parens in case it's followed by "= 1"
            var clause = element.Parent;
            var parens = element.NextNonWsSibling();
            clause.RemoveChild(element);
            parens.Name = SqlStructureConstants.ENAME_EXPRESSION_PARENS;
            parens.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "is");
            parens.AddChild(SqlStructureConstants.ENAME_DATATYPE_KEYWORD, "json");
        }

        if (element.Matches(SqlStructureConstants.ENAME_OTHERNODE, "openjson"))
        {
            // openjson implementation depends
            // 1) if with clause is not found, json_each() is used. 
            // If path is specified, json is additionally wrapped in json_query()
            // 2) if with clause is found, json_table() is used

            var selectionTarget = element.Parent;
            var fromClause = selectionTarget.Parent;
            var statement = fromClause.Parent;
            var withKeyword = selectionTarget.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "with");
            var columnList = selectionTarget.ChildByName(SqlStructureConstants.ENAME_EXPRESSION_PARENS);
            var parens = element.NextNonWsSibling();
            var args = parens.Children.ToList();
            var comma = parens.ChildByName(SqlStructureConstants.ENAME_COMMA);
            var commaIndex = args.IndexOf(comma);

            var jsonNodes = args.Take(commaIndex > -1 ? commaIndex : args.Count).ToList();

            Node? path = null;
            var _path = comma?.NextNonWsSibling();
            if (_path?.Matches(SqlStructureConstants.ENAME_STRING) ?? false)
            {
                path = _path;
            }

            if (path != null && withKeyword == null)
            {
                parens.AddChild(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "json_query");
                var pathParens = parens.AddChild(SqlStructureConstants.ENAME_FUNCTION_PARENS, "");
                foreach (var node in args)
                {
                    parens.RemoveChild(node);
                    pathParens.AddChild(node);
                }

                parens = pathParens;
            }

            if (withKeyword != null)
            {
                element.TextValue = "json_table";
                selectionTarget.RemoveChild(withKeyword);
                selectionTarget.RemoveChild(columnList);

                //path is necessary for json_table
                if (path == null)
                {
                    parens.AddChild(SqlStructureConstants.ENAME_COMMA, ",");
                    parens.AddChild(SqlStructureConstants.ENAME_STRING, "$");
                }

                var columnsClause = parens.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");

                columnsClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "columns");
                columnsClause.AddChild(columnList);

                //treat function parens as expression parens for better indentation, probably better handled in some other way
                parens.Name = SqlStructureConstants.ENAME_EXPRESSION_PARENS;

                var pathNodes = columnList.ChildrenByName(SqlStructureConstants.ENAME_STRING).ToList();
                foreach (var node in pathNodes) {
                    columnList.InsertChildBefore(SqlStructureConstants.ENAME_OTHERKEYWORD, "path", node);
                }

                var asNodes = columnList.Children.Where(e => e.Name == SqlStructureConstants.ENAME_OTHERKEYWORD && e.TextValue.ToLower() == "as");
                foreach (var asNode in asNodes) {
                    var jsonNode = asNode.NextNonWsSibling();
                    if (!jsonNode.Matches(SqlStructureConstants.ENAME_OTHERNODE, "json")) {
                        continue;
                    }

                    asNode.TextValue = "with";
                    jsonNode.TextValue = "wrapper";
                }
            }
            else
            {
                element.TextValue = "json_each";
            }
        }
        
        foreach (var child in new List<Node>(element.Children)) {
            ConvertJsonFunctions(child);
        }
    }

    private List<Node> BuildJsonObject(Dictionary<string, object> data) {
        List<Node> result = [];

        result.Add(new NodeImpl {
            Name = SqlStructureConstants.ENAME_FUNCTION_KEYWORD,
            TextValue = "json_object"
        });

        var parens = new NodeImpl {
            Name = SqlStructureConstants.ENAME_FUNCTION_PARENS,
            TextValue = ""  
        };

        result.Add(parens);

        var lastItem = data.Last();
        foreach (var item in data) {
            parens.AddChild(SqlStructureConstants.ENAME_STRING, item.Key);
            parens.AddChild(SqlStructureConstants.ENAME_PERIOD, ":");
            parens.AddChild(SqlStructureConstants.ENAME_PERIOD, " ");
            if (item.Value is List<Node> valueNodes) {
                foreach (var node in valueNodes) {
                    parens.AddChild(node);
                }
            } else {
                foreach (var node in BuildJsonObject(item.Value as Dictionary<string, object> ?? new())) {
                    parens.AddChild(node);
                }
            }

            if (!item.Equals(lastItem)) {
                parens.AddChild(SqlStructureConstants.ENAME_COMMA, ",");
            }
        }
        
        return result;
    }

    private void ConvertForJsonPath(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "for"))
        {
            var json = element.NextNonWsSibling();
            if (!json?.Matches(SqlStructureConstants.ENAME_OTHERNODE, "json") ?? true)
            {
                return;
            }

            var path = json?.NextNonWsSibling();
            if ((!path?.Matches(SqlStructureConstants.ENAME_OTHERNODE, "path") ?? true) && (!path?.Matches(SqlStructureConstants.ENAME_OTHERNODE, "auto") ?? true))
            {
                return;
            }

            var isJsonAuto = path!.Matches(SqlStructureConstants.ENAME_OTHERNODE, "auto");

            var comma = path?.NextNonWsSibling();
            var withoutArrayWrapperNode = comma?.NextNonWsSibling();
            var withoutArrayWrapper = false;
            if (withoutArrayWrapperNode?.Matches(SqlStructureConstants.ENAME_OTHERNODE, "without_array_wrapper") ?? false)
            {
                withoutArrayWrapper = true;
            }

            var forClause = element.Parent;
            var statement = forClause.Parent;
            statement.RemoveChild(forClause);

            var statementClauses = statement.Children.ToList();
            foreach (var clause in statementClauses) {
                statement.RemoveChild(clause);
            }

            var jsonSelectClause = statement.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
            jsonSelectClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "select");
            jsonSelectClause.AddChild(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, withoutArrayWrapper ? "to_json" : "json_agg");
            var toJsonParens = jsonSelectClause.AddChild(SqlStructureConstants.ENAME_FUNCTION_PARENS, "");
            toJsonParens.AddChild(SqlStructureConstants.ENAME_OTHERNODE, "_to_json_temp");
            if (isJsonAuto) {
                jsonSelectClause.AddChild(
                    SqlStructureConstants.ENAME_COMMENT_MULTILINE,
                    "CONVERTER WARNING: converted from /for json auto/ as if it was /for json path/. Output is potentially different, especially if query has joins"
                );

                WarningCount += 1;
            }
            
            var jsonFromClause = statement.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
            jsonFromClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "from");
            var selectionTarget = jsonFromClause.AddChild(SqlStructureConstants.ENAME_SELECTIONTARGET, "");
            var queryParens = selectionTarget.AddChild(SqlStructureConstants.ENAME_SELECTIONTARGET_PARENS, "");
            foreach (var clause in statementClauses) {
                queryParens.AddChild(clause);
            }
            selectionTarget.AddChild(SqlStructureConstants.ENAME_OTHERNODE, "_to_json_temp");

            var selectClause = queryParens.Children.First(e => e.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "select") != null);
            var columns = GetSelectColumns(selectClause);
            var compositeNameColumns = columns.FindAll(e => e.FirstOrDefault(e1 => e1.Name == SqlStructureConstants.ENAME_BRACKET_QUOTED_NAME && e1.TextValue.Contains('.')) != null);

            Dictionary<string, object> nestedJsonColumns = new();

            foreach (var column in compositeNameColumns)
            {
                var lastNode = column.Last();
                foreach (var node in column)
                {
                    if (node == lastNode)
                    {
                        var columnComma = node.NextNonWsSibling();
                        if (columnComma != null)
                        {
                            selectClause.RemoveChild(columnComma);
                        }
                    }
                    selectClause.RemoveChild(node);
                }

                var name = column.First(e1 => e1.Name == SqlStructureConstants.ENAME_BRACKET_QUOTED_NAME && e1.TextValue.Contains('.')).TextValue;
                var nameSplit = name.Split(".");
                var varName = nameSplit.Last();
                var currentObject = nestedJsonColumns;

                var asKeyword = column.First(e => e.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "as"));
                var asIndex = column.IndexOf(asKeyword);
                var value = column.Take(asIndex).Where(e => e.Name != SqlStructureConstants.ENAME_WHITESPACE).ToList();

                foreach (var namePart in nameSplit)
                {
                    if (namePart == varName)
                    {
                        currentObject![namePart] = value;
                    }
                    else
                    {
                        if (!currentObject!.ContainsKey(namePart))
                        {
                            currentObject[namePart] = new Dictionary<string, object>();
                        }
                        currentObject = currentObject[namePart] as Dictionary<string, object>;
                    }
                }
            }

            if (columns.Count != compositeNameColumns.Count) {
                selectClause.AddChild(SqlStructureConstants.ENAME_COMMA, ",");
            }
            
            var lastColumn = nestedJsonColumns.LastOrDefault();
            foreach (var column in nestedJsonColumns) {
                var nodes = BuildJsonObject(column.Value as Dictionary<string, object> ?? new());
                foreach (var node in nodes) {
                    selectClause.AddChild(node);
                }

                selectClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "as");
                selectClause.AddChild(SqlStructureConstants.ENAME_OTHERNODE, column.Key);

                if (!column.Equals(lastColumn)) {
                    selectClause.AddChild(SqlStructureConstants.ENAME_COMMA, ",");
                }
            }
        }

        foreach (var child in new List<Node>(element.Children)) {
            ConvertForJsonPath(child);
        }
    }

    private (Node name, List<Node> value, SelectColumnType type) ParseSelectColumn(List<Node> nodes) {
        var asKeyword = nodes.FirstOrDefault(e => e.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "as"));
        var name = nodes.Last(e => e.IsName());
        List<Node>? value = null;
        var nonWsNodes = nodes.FindAll(e => e.Name != SqlStructureConstants.ENAME_WHITESPACE);
        SelectColumnType? type = null;
        
        if (asKeyword != null) {
            value = nodes.Take(nodes.IndexOf(asKeyword)).ToList();
        } else {
            var hasPeriod = name.PreviousNonWsSibling()?.Matches(SqlStructureConstants.ENAME_PERIOD) ?? false;
            var isOneNode = nonWsNodes.Count == 1;

            
            if (hasPeriod || isOneNode) {
                value = nodes;

                if (hasPeriod)
                {
                    type = SelectColumnType.NameAndTableOnly;
                }
                else if (isOneNode)
                {
                    type = SelectColumnType.NameOnly;
                }
            }
            else {
                value = nodes.FindAll(e => e != name);
            }
        }

        if (type == null) {
            var trimmed = TrimNodeList(value);
            if (value.Any(e => e.Name != SqlStructureConstants.ENAME_WHITESPACE && e.Name != SqlStructureConstants.ENAME_OTHERNODE && e.Name != SqlStructureConstants.ENAME_PERIOD)) {
                type = SelectColumnType.Computed;
            } else {
                type = SelectColumnType.Aliased;
            }
        }
        
        return (name, value, type.Value);
    }

    private void ConvertPivot(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_OTHERNODE, "pivot")) {
            var pivotParens = element.NextNonWsSibling();
            var tableAlias = pivotParens.NextNonWsSibling();
            var pivotColumnList = pivotParens.ChildByName(SqlStructureConstants.ENAME_IN_PARENS);
            var pivotColumns = pivotColumnList.Children.Where(e => e.IsName()).Select(e => e.TextValue.ToLower());
            var pivotForKeyword = pivotParens.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "for")!;
            var pivotForIndex = pivotParens.Children.ToList().IndexOf(pivotForKeyword);
            var pivotValue = pivotParens.Children.Take(pivotForIndex).ToList();
            var pivotSourceColumn = pivotForKeyword.NextNonWsSibling();

            var pivotClause = element.Parent;
            var statement = pivotClause.Parent;
            var fromClause = statement.Children.First(e => e.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "from") != null);
            statement.RemoveChild(pivotClause);

            var selectClause = statement.Children.First(e => e.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "select") != null);
            var selectColumns = GetSelectColumns(selectClause);

            foreach (var column in selectColumns) {
                var firstNode = column.First();
                var parsed = ParseSelectColumn(column);
                if (pivotColumns.Contains(parsed.name.TextValue.ToLower())) {
                    foreach (var node in pivotValue) {
                        selectClause.InsertChildBefore((Node)node.Clone(), firstNode);
                    }
                    selectClause.InsertChildBefore(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "filter", firstNode);
                    var filterParens = selectClause.InsertChildBefore(SqlStructureConstants.ENAME_FUNCTION_PARENS, "", firstNode);
                    filterParens.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "where");
                    filterParens.AddChild((Node)pivotSourceColumn.Clone());
                    filterParens.AddChild(SqlStructureConstants.ENAME_EQUALSSIGN, "=");
                    filterParens.AddChild(SqlStructureConstants.ENAME_STRING, parsed.name.TextValue.ToLower());
                    selectClause.InsertChildBefore(SqlStructureConstants.ENAME_OTHERKEYWORD, "as", firstNode);
                }
            }
        }

        foreach (var child in new List<Node>(element.Children)) {
            ConvertPivot(child);
        }
    }

    private void ConvertUnpivot(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_OTHERNODE, "unpivot")) {
            var unpivotClause = element.Parent;
            var selectionTarget = unpivotClause.PreviousNonWsSibling().ChildByName(SqlStructureConstants.ENAME_SELECTIONTARGET);
            var tableName = selectionTarget.ChildByName(SqlStructureConstants.ENAME_OTHERNODE);
            var unpivotParens = element.NextNonWsSibling();
            var fieldListParens = unpivotParens.ChildByName(SqlStructureConstants.ENAME_IN_PARENS);
            var valueColumnName = unpivotParens.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERNODE)!;
            var nameColumnName = valueColumnName.NextNonWsSibling().NextNonWsSibling();
            var tableAlias = unpivotParens.NextNonWsSibling();
            var joinOn = tableAlias.NextNonWsSibling();

            selectionTarget.RemoveChild(tableName);
            tableAlias.Parent.RemoveChild(tableAlias);
            var selectionTargetParens = selectionTarget.AddChild(SqlStructureConstants.ENAME_SELECTIONTARGET_PARENS, "");
            selectionTarget.AddChild(tableAlias);
            if (joinOn != null) {
                joinOn.Parent.RemoveChild(joinOn);
                selectionTarget.AddChild(joinOn);
            }
            
            var selectClause = selectionTargetParens.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
            selectClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "select");
            selectClause.AddChild(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "unnest");
            var unnestParens = selectClause.AddChild(SqlStructureConstants.ENAME_FUNCTION_PARENS, "");
            unnestParens.AddChild(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "array");
            unnestParens.AddChild(SqlStructureConstants.ENAME_PERIOD, "[");
            foreach (var node in fieldListParens.Children) {
                var clone = (Node)node.Clone();
                if (clone.IsName()) {
                    clone.Name = SqlStructureConstants.ENAME_STRING;
                }
                unnestParens.AddChild(clone);
            }
            unnestParens.AddChild(SqlStructureConstants.ENAME_PERIOD, "]");
            selectClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "as");
            selectClause.AddChild(SqlStructureConstants.ENAME_OTHERNODE, nameColumnName.TextValue);

            selectClause.AddChild(SqlStructureConstants.ENAME_COMMA, ",");


            selectClause.AddChild(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "unnest");
            unnestParens = selectClause.AddChild(SqlStructureConstants.ENAME_FUNCTION_PARENS, "");
            unnestParens.AddChild(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "array");
            unnestParens.AddChild(SqlStructureConstants.ENAME_PERIOD, "[");
            foreach (var node in fieldListParens.Children) {
                var clone = (Node)node.Clone();
                unnestParens.AddChild(clone);
            }
            unnestParens.AddChild(SqlStructureConstants.ENAME_PERIOD, "]");
            selectClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "as");
            selectClause.AddChild(SqlStructureConstants.ENAME_OTHERNODE, valueColumnName.TextValue);
            
            var fromClause = selectionTargetParens.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
            fromClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "from");
            var fromClauseTarget = fromClause.AddChild(SqlStructureConstants.ENAME_SELECTIONTARGET, "");
            fromClauseTarget.AddChild(tableName);

            unpivotClause.Parent.RemoveChild(unpivotClause);
        }
    
        foreach (var child in new List<Node>(element.Children)) {
            ConvertUnpivot(child);
        }
    }

    private List<string> ConvertTableVariables(Node element, List<string>? arrayVariables = null) {
        if (arrayVariables == null) arrayVariables = [];
    
        //for input parameters - convert to array
        if (element.Matches(SqlStructureConstants.ENAME_DDL_PARENS)) {
            var children = element.Children.ToList();
            var readonlyKeywords = children.FindAll(e => e.Matches(SqlStructureConstants.ENAME_OTHERNODE, "readonly"));
            foreach (var kw in readonlyKeywords) {
                var typeName = kw.PreviousNonWsSibling();
                var period = typeName?.PreviousSibling();
                var schema = period?.PreviousSibling();
                if (schema == null) continue;

                element.RemoveChild(schema);
                element.RemoveChild(period!);
                element.RemoveChild(kw);
                element.InsertChildAfter(SqlStructureConstants.ENAME_PERIOD, "[]", typeName!);

                arrayVariables.Add(typeName!.TextValue);
            }
        }

        //for variables declared as table - convert to temp table
        if (element.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "table")) {
            if (!element.Parent.Matches(SqlStructureConstants.ENAME_DDL_DECLARE_BLOCK)) return [];
            var clause = element.Parent;
            clause.Name = SqlStructureConstants.ENAME_DDL_OTHER_BLOCK;
            var declare = clause.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "declare")!;
            declare.TextValue = "create";
            clause.InsertChildAfter(SqlStructureConstants.ENAME_OTHERKEYWORD, "temp", declare);
            var tableName = element.PreviousNonWsSibling();
            tableName.TextValue = tableName.TextValue.Replace("@", "_");
            clause.RemoveChild(tableName);
            clause.InsertChildAfter(tableName, element);
        }

        //for variables declared as table type - convert to array
        //implemented in AddDeclareSection
         
        foreach (var child in new List<Node>(element.Children)) {
            ConvertTableVariables(child, arrayVariables);
        }

        return arrayVariables;
    }

    private void UnnestArrays(Node element, List<string>? arrayVariables) {
        if (element.Matches(SqlStructureConstants.ENAME_SELECTIONTARGET)) {
            var tableName = element.Children.First(e => e.Name == SqlStructureConstants.ENAME_WHITESPACE);
            var tableAlias = tableName.NextNonWsSibling();
            if (!(arrayVariables?.Any(e => e.ToLower() == tableName.TextValue.ToLower()) ?? false)) {
                return;
            }

            element.InsertChildBefore(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "unnest", tableName);
            var parens = element.InsertChildBefore(SqlStructureConstants.ENAME_FUNCTION_PARENS, "", tableName);
            element.RemoveChild(tableName);
            parens.AddChild(tableName);
        }

        foreach (var child in new List<Node>(element.Children)) {
            UnnestArrays(child, arrayVariables);
        }
    }

    private void InsertIntoArrays(Node element, List<string>? arrayVariables) {
        if (arrayVariables == null) arrayVariables = [];
        if (element.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "insert")) {
            var insertClause = element.Parent.Parent;
            var tableName = insertClause.ChildByName(SqlStructureConstants.ENAME_OTHERNODE);
            if (!arrayVariables.Any(e => e.ToLower() == tableName.TextValue.ToLower())) {
                return;
            }
            
            var insertStatement = insertClause.Parent;
            var insertClauseIndex = insertStatement.Children.ToList().IndexOf(insertClause);
            var tail = insertStatement.Children.Skip(insertClauseIndex + 1).ToList(); 
            var valuesClause = insertStatement.Children.FirstOrDefault(e => e.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "values") != null);
            if (valuesClause != null) {
                var valuesNodes = new List<Node>(valuesClause.Children);
                var firstChild = valuesClause.Children.First();
                var fromKeyword = valuesClause.InsertChildBefore(SqlStructureConstants.ENAME_OTHERKEYWORD, "from", firstChild);
                var selectionTarget = valuesClause.InsertChildAfter(SqlStructureConstants.ENAME_SELECTIONTARGET, "", fromKeyword);
            
                var selectionTargetParens = selectionTarget.AddChild(SqlStructureConstants.ENAME_SELECTIONTARGET_PARENS, "");
                var selectionTargetClause = selectionTargetParens.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
                foreach (var node in valuesNodes) {
                    valuesClause.RemoveChild(node);
                    selectionTargetClause.AddChild(node);
                }
                selectionTarget.AddChild(SqlStructureConstants.ENAME_OTHERNODE, "_t");
            } else {
                var selectWrapperClause = insertStatement.InsertChildAfter(SqlStructureConstants.ENAME_SQL_CLAUSE, "", insertClause);
                selectWrapperClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "from");
                var selectWrapper = selectWrapperClause.AddChild(SqlStructureConstants.ENAME_SELECTIONTARGET_PARENS, "");
                selectWrapperClause.AddChild(SqlStructureConstants.ENAME_OTHERNODE, "_t");
                foreach (var node in tail) {
                    node.Parent.RemoveChild(node);
                    selectWrapper.AddChild(node);
                }
            }

            var updatedTail = insertStatement.Children.Skip(insertClauseIndex + 1).ToList();
            
            var assignStatement = insertStatement.Parent.InsertChildBefore(SqlStructureConstants.ENAME_SQL_STATEMENT, "", insertStatement);
            var assignClause = assignStatement.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
            assignClause.AddChild((Node)tableName.Clone());
            assignClause.AddChild(SqlStructureConstants.ENAME_EQUALSSIGN, ":=");
            assignClause.AddChild(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "array_cat");
            
            var arrayConcatParens = assignClause.AddChild(SqlStructureConstants.ENAME_FUNCTION_PARENS, "");
            arrayConcatParens.AddChild((Node)tableName.Clone());
            arrayConcatParens.AddChild(SqlStructureConstants.ENAME_COMMA, ",");

            var newArrayParens = arrayConcatParens.AddChild(SqlStructureConstants.ENAME_EXPRESSION_PARENS, "");
            var arrayAggClause = newArrayParens.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
            arrayAggClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "select");
            arrayAggClause.AddChild(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "array_agg");
            var arrayAggParens = arrayAggClause.AddChild(SqlStructureConstants.ENAME_FUNCTION_PARENS, "");
            arrayAggParens.AddChild(SqlStructureConstants.ENAME_OTHERNODE, "_t");
            
            foreach (var clause in updatedTail) {
                newArrayParens.AddChild((Node)clause.Clone());
            }

            insertStatement.Parent.RemoveChild(insertStatement);
        }

        foreach (var child in new List<Node>(element.Children)) {
            InsertIntoArrays(child, arrayVariables);
        }
    }

    private void ConvertFormatFunction(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_OTHERNODE, "format")) {
            element.TextValue = "to_char";
            var parens = element.NextNonWsSibling();
            var formatString = parens.ChildByName(SqlStructureConstants.ENAME_STRING);
            var isNumber = formatString.TextValue.Contains("0") || formatString.TextValue.Contains("#");
            if (isNumber) {
                formatString.TextValue = "FM" + formatString.TextValue.Replace("#", "9").Replace(".", "D");
            } else {
                foreach (var entry in DateMapping) {
                    formatString.TextValue = formatString.TextValue.Replace(entry.Key, entry.Value);
                }
            }
        }
    
        foreach (var child in new List<Node>(element.Children)) {
            ConvertFormatFunction(child);
        }
    }

    private void ConvertDirectlyMappedFunctions(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_OTHERNODE) || element.Matches(SqlStructureConstants.ENAME_FUNCTION_KEYWORD)) {
            if (DirectlyMappedFunctions.ContainsKey(element.TextValue.ToLower())) {
                element.TextValue = DirectlyMappedFunctions[element.TextValue.ToLower()];
            }
        }
    
        foreach (var child in new List<Node>(element.Children)) {
            ConvertDirectlyMappedFunctions(child);
        }
    }

    private void ConvertStringAggFunction(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_OTHERNODE, "string_agg")) {
            var clause = element.Parent;
            var within = clause.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERNODE, "within");
            if (within == null) return;

            var withinClause = clause.NextNonWsSibling();
            var withinParens = withinClause.ChildByName(SqlStructureConstants.ENAME_EXPRESSION_PARENS);
            clause.RemoveChild(within);
            clause.Parent.RemoveChild(withinClause);

            var stringAggParens = element.NextNonWsSibling();
            foreach (var _clause in withinParens.Children) {
                foreach (var node in new List<Node>(_clause.Children)) {
                    node.Parent.RemoveChild(node);
                    stringAggParens.AddChild(node);
                }
            }
        }
    
        foreach (var child in new List<Node>(element.Children)) {
            ConvertStringAggFunction(child);
        }
    }

    private void ConvertForXmlPathStringAgg(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_DATATYPE_KEYWORD, "xml")) {
            var path = element.NextNonWsSibling();
            if (!(path?.Matches(SqlStructureConstants.ENAME_OTHERNODE, "path") ?? false)) {
                return;
            }

            var forXmlPathParens = path.NextNonWsSibling();
            if (!(forXmlPathParens?.Matches(SqlStructureConstants.ENAME_FUNCTION_PARENS) ?? false)) {
                return;
            }

            var str = forXmlPathParens.ChildByNameAndText(SqlStructureConstants.ENAME_STRING, "");
            if (str == null) {
                return;
            }

            var clause = element.Parent;
            var expression = clause.Parent;
            var stuffParens = expression.Parent;
            var stuffFunction = stuffParens.PreviousNonWsSibling();
            var usingStuff = false;
            if (stuffParens.Matches(SqlStructureConstants.ENAME_FUNCTION_PARENS)
                && (stuffFunction?.Matches(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "stuff") ?? false)) {
                usingStuff = true;
            }

            // if stuff() function is used, attempt to use string_agg() with real separator and remove stuff()
            // otherwise use string_agg with '' as separator

            var selectClause = expression.Children.First(e => e.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "select") != null);
            expression.RemoveChild(clause);
            var separator = "";
            if (usingStuff) {
                var separatorNode = selectClause.ChildByName(SqlStructureConstants.ENAME_STRING);
                var plusSign = separatorNode.NextNonWsSibling();

                if (separatorNode != null && plusSign.Matches(SqlStructureConstants.ENAME_OTHEROPERATOR, "+")) {
                    separator = separatorNode.TextValue;
                    selectClause.RemoveChild(separatorNode);
                    selectClause.RemoveChild(plusSign);
                }
            }

            var selectKeyword = selectClause.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "select");
            var selectNodes = selectClause.Children.Where(e => e != selectKeyword).ToList();

            selectClause.AddChild(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "string_agg");
            var stringAggParens = selectClause.AddChild(SqlStructureConstants.ENAME_FUNCTION_PARENS, "");
            foreach (var node in selectNodes) {
                selectClause.RemoveChild(node);
                stringAggParens.AddChild(node);
            }

            stringAggParens.AddChild(SqlStructureConstants.ENAME_COMMA, ",");
            stringAggParens.AddChild(SqlStructureConstants.ENAME_STRING, separator);

            if (usingStuff) {
                stuffParens.RemoveChild(expression);
                stuffParens.Parent.InsertChildAfter(expression, stuffParens);
                stuffFunction!.Parent.RemoveChild(stuffFunction);
                stuffParens.Parent.RemoveChild(stuffParens);
            }
        }
    
        foreach (var child in new List<Node>(element.Children)) {
            ConvertForXmlPathStringAgg(child);
        }
    }

    private void ConvertStuffFunction(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "stuff")) {
            var parens = element.NextNonWsSibling();
            var commas = parens.ChildrenByName(SqlStructureConstants.ENAME_COMMA).ToList();

            var start = commas[0].NextNonWsSibling();
            var length = commas[1].NextNonWsSibling();
            var replacement = commas[2].NextNonWsSibling();

            element.TextValue = "overlay";
            commas[0].Name = SqlStructureConstants.ENAME_OTHERKEYWORD;
            commas[0].TextValue = "placing";
            commas[1].Name = SqlStructureConstants.ENAME_OTHERKEYWORD;
            commas[1].TextValue = "from";
            commas[2].Name = SqlStructureConstants.ENAME_OTHERKEYWORD;
            commas[2].TextValue = "for";
            parens.RemoveChild(start);
            parens.RemoveChild(length);
            parens.RemoveChild(replacement);

            parens.InsertChildAfter(replacement, commas[0]);
            parens.InsertChildAfter(start, commas[1]);
            parens.InsertChildAfter(length, commas[2]);
        }
    
        foreach (var child in new List<Node>(element.Children)) {
            ConvertStuffFunction(child);
        }
    }

    private void ConvertDateDiffFunction(Node element)
    {
        if (element.Matches(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "datediff")) {
            element.TextValue = "extract";
            var parens = element.NextNonWsSibling();
            var datePart = parens.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERNODE);
            var comma = parens.ChildByNameAndText(SqlStructureConstants.ENAME_COMMA)!;

            comma.Name = SqlStructureConstants.ENAME_OTHERKEYWORD;
            comma.TextValue = "from";

            var comma2 = parens.ChildByNameAndText(SqlStructureConstants.ENAME_COMMA)!;
            comma2.Name = SqlStructureConstants.ENAME_OTHEROPERATOR;
            comma2.TextValue = "-";

            var date1 = comma.NextNonWsSibling();
            var date1Details = date1.NextNonWsSibling();
            var date2 = comma2.NextNonWsSibling();
            var date2Details = date2.NextNonWsSibling();

            parens.RemoveChild(date1);
            parens.RemoveChild(date2);
            parens.InsertChildBefore(date2, comma2);
            parens.InsertChildAfter(date1, comma2);

            if (date1Details != null && date1Details.Matches(SqlStructureConstants.ENAME_FUNCTION_PARENS)) {
                parens.RemoveChild(date1Details);
                parens.InsertChildAfter(date1Details, date1);
            }

            if (date2Details != null && date2Details.Matches(SqlStructureConstants.ENAME_FUNCTION_PARENS)) {
                parens.RemoveChild(date2Details);
                parens.InsertChildAfter(date2Details, date2);
            }
        }

        foreach (var child in new List<Node>(element.Children))
        {
            ConvertDateDiffFunction(child);
        }
    }


    private void ConvertDatePartFunction(Node element)
    {
        if (element.Matches(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "datepart")) {
            element.TextValue = "extract";
            var parens = element.NextNonWsSibling();
            var comma = parens.ChildByName(SqlStructureConstants.ENAME_COMMA);
            comma.Name = SqlStructureConstants.ENAME_OTHERKEYWORD;
            comma.TextValue = "from";
        }

        foreach (var child in new List<Node>(element.Children))
        {
            ConvertDatePartFunction(child);
        }
    }

    private void ConvertDateAddFunction(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "dateadd")) {
            var clause = element.Parent;
            var parens = element.NextNonWsSibling();
            var part = parens.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERNODE)!;
            var comma1 = parens.ChildByNameAndText(SqlStructureConstants.ENAME_COMMA)!;
            var mod = comma1.NextNonWsSibling();
            var comma2 = mod.NextNonWsSibling();
            var date = comma2.NextNonWsSibling();
            var dateDetails = date.NextNonWsSibling();

            parens.RemoveChild(date);
            var newParens = clause.InsertChildBefore(SqlStructureConstants.ENAME_EXPRESSION_PARENS, "", element);
            newParens.AddChild(date);
            if (dateDetails != null && dateDetails.Matches(SqlStructureConstants.ENAME_FUNCTION_PARENS)) {
                parens.RemoveChild(dateDetails);
                newParens.AddChild(dateDetails);
            }

            newParens.AddChild(SqlStructureConstants.ENAME_OTHEROPERATOR, "+");
            newParens.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "interval");
            newParens.AddChild(SqlStructureConstants.ENAME_STRING, $"{mod.TextValue} {part.TextValue}");

            clause.RemoveChild(element);
            clause.RemoveChild(parens);
        }
            
        foreach (var child in new List<Node>(element.Children))
        {
            ConvertDateAddFunction(child);
        }
    }

    private void ConvertIifFunction(Node element)
    {
        if (element.Matches(SqlStructureConstants.ENAME_OTHERNODE, "iif")) {
            var parens = element.NextNonWsSibling();
            var commas = parens.ChildrenByName(SqlStructureConstants.ENAME_COMMA).ToList();
            var nodeList = parens.Children.ToList();
            var comma1Index = nodeList.IndexOf(commas[0]);
            var comma2Index = nodeList.IndexOf(commas[1]);
            var condition = nodeList.Take(comma1Index);
            var truePart = nodeList.Skip(comma1Index + 1).Take(comma2Index - comma1Index);
            var falsePart = nodeList.Skip(comma2Index + 1);

            var clause = element.Parent;

            var caseStatement = clause.InsertChildBefore(SqlStructureConstants.ENAME_CASE_STATEMENT, "", element);
            var opener = caseStatement.AddChild(SqlStructureConstants.ENAME_CONTAINER_OPEN, "");
            opener.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "case");
            caseStatement.AddChild(SqlStructureConstants.ENAME_CASE_INPUT, "");
            
            var caseWhen = caseStatement.AddChild(SqlStructureConstants.ENAME_CASE_WHEN, "");
            var whenOpener = caseWhen.AddChild(SqlStructureConstants.ENAME_CONTAINER_OPEN, "");
            whenOpener.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "when");
            var caseWhenBody = caseWhen.AddChild(SqlStructureConstants.ENAME_CONTAINER_GENERALCONTENT, "");
            foreach (var node in condition) {
                node.Parent.RemoveChild(node);
                caseWhenBody.AddChild(node);
            }
            
            var then = caseWhen.AddChild(SqlStructureConstants.ENAME_CASE_THEN, "");
            var thenOpener = then.AddChild(SqlStructureConstants.ENAME_CONTAINER_OPEN, "");
            thenOpener.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "then");
            var thenBody = then.AddChild(SqlStructureConstants.ENAME_CONTAINER_GENERALCONTENT, "");
            foreach(var node in truePart) {
                node.Parent.RemoveChild(node);
                thenBody.AddChild(node);
            }
            
            var caseElse = caseStatement.AddChild(SqlStructureConstants.ENAME_CASE_ELSE, "");
            var elseOpener = caseElse.AddChild(SqlStructureConstants.ENAME_CONTAINER_OPEN, "");
            elseOpener.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "else");
            var elseBody = caseElse.AddChild(SqlStructureConstants.ENAME_CONTAINER_GENERALCONTENT, "");
            foreach (var node in falsePart) {
                node.Parent.RemoveChild(node);
                elseBody.AddChild(node);
            }

            var closer = caseStatement.AddChild(SqlStructureConstants.ENAME_CONTAINER_CLOSE, "");
            closer.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "end");

            clause.RemoveChild(element);
            clause.RemoveChild(parens);
        }

        foreach (var child in new List<Node>(element.Children))
        {
            ConvertIifFunction(child);
        }
    }

    private void ConvertDataTypes(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_DATATYPE_KEYWORD)) {
            if (element.Matches(SqlStructureConstants.ENAME_DATATYPE_KEYWORD, "varchar") 
                || element.Matches(SqlStructureConstants.ENAME_DATATYPE_KEYWORD, "nvarchar")
            ) {
                var parens = element.NextNonWsSibling();
                if (parens?.Matches(SqlStructureConstants.ENAME_DDLDETAIL_PARENS) ?? false) {
                    parens.Parent.RemoveChild(parens);
                }
            }

            if (DataTypeMapping.ContainsKey(element.TextValue.ToLower())) {
                element.TextValue = DataTypeMapping[element.TextValue.ToLower()];
            }
        }
    
        foreach (var child in new List<Node>(element.Children)) {
            ConvertDataTypes(child);
        }
    }

    private void ConvertProcedureCalls(Node element, Dictionary<string, Node> tempTableDefinitions, List<(List<Node> variable, List<Node> value)> declarations) {
        if (element.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "exec")) {
            element.TextValue = "call";
            var clause = element.Parent;
            var name = element.NextNonWsSibling();
            var nameIndex = clause.Children.ToList().IndexOf(name);
            var arguments = clause.Children.Skip(nameIndex + 1).ToList();
            var equalsSigns = clause.ChildrenByName(SqlStructureConstants.ENAME_EQUALSSIGN);
            foreach (var node in equalsSigns) {
                node.TextValue = "=>";
            }
            
            var parens = clause.InsertChildAfter(SqlStructureConstants.ENAME_FUNCTION_PARENS, "", name);
            foreach (var node in arguments) {
                clause.RemoveChild(node);
                parens.AddChild(node);
            }

            var execStatement = clause.Parent;
            var insertClause = execStatement.Children.FirstOrDefault(c => c.ChildByName(SqlStructureConstants.ENAME_COMPOUNDKEYWORD) != null);
            if (insertClause == null) return;

            var insertStatement = execStatement.Parent.InsertChildAfter(SqlStructureConstants.ENAME_SQL_STATEMENT, "", execStatement);
            execStatement.RemoveChild(insertClause);
            insertStatement.AddChild(insertClause);

            var tableName = insertClause.ChildByName(SqlStructureConstants.ENAME_OTHERNODE).TextValue.ToLower();
            Node? tableDefinition = null;

            var isTableVariable = false;
            var isTypeVariable = false;
            var isTempTable = tempTableDefinitions.ContainsKey(tableName);
            if (isTempTable) {
                tableDefinition = tempTableDefinitions[tableName];
            }

            if (!isTempTable) {
                isTableVariable = declarations.Any(v =>
                    v.variable.Any(n => n.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "table"))
                    && v.variable.Any(n => n.Matches(SqlStructureConstants.ENAME_OTHERNODE, tableName))
                );
            }

            if (isTableVariable)
            {
                tableDefinition = declarations.Find(v =>
                    v.variable.Any(n => n.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "table"))
                    && v.variable.Any(n => n.Matches(SqlStructureConstants.ENAME_OTHERNODE, tableName))
                ).variable.Find(e => e.Matches(SqlStructureConstants.ENAME_DDL_PARENS));
            }

            if (!isTempTable && !isTableVariable)
            {
                isTypeVariable = declarations.Any(v =>
                    !v.variable.Any(n => n.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "table"))
                    && v.variable.Any(n => n.Matches(SqlStructureConstants.ENAME_OTHERNODE, tableName))
                );
            }

            if (isTypeVariable) {
                tableDefinition = declarations.Find(v =>
                    !v.variable.Any(n => n.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "table"))
                    && v.variable.Any(n => n.Matches(SqlStructureConstants.ENAME_OTHERNODE, tableName))
                ).variable.Last(e => e.Matches(SqlStructureConstants.ENAME_OTHERNODE));
            }

            var selectClause = insertStatement.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
            selectClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "select");
            selectClause.AddChild(SqlStructureConstants.ENAME_ASTERISK, "*");
            var fromClause = insertStatement.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
            fromClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "from");
            var selectionTarget = fromClause.AddChild(SqlStructureConstants.ENAME_SELECTIONTARGET, "");

            if (tableDefinition == null) {
                // if tableDefinition is unknown, we're likely inserting into a temp table that was defined somewhere else
                // so we treat it like a normal temp table but columns list has to be a placeholder with TODO comment

                selectionTarget.AddChild(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "fetch_all_from");
                var parens1 = selectionTarget.AddChild(SqlStructureConstants.ENAME_FUNCTION_PARENS, "");
                parens1.AddChild(SqlStructureConstants.ENAME_STRING, $"{name.TextValue}_select1");
                selectionTarget.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "as");
                var tableDefinitionStub = selectionTarget.AddChild(SqlStructureConstants.ENAME_EXPRESSION_PARENS, "");
                tableDefinitionStub.AddChild(SqlStructureConstants.ENAME_COMMENT_MULTILINE, "converter warning: (TODO) inserting into a table with unknown columns. Add them here");
            }
            else if (tableDefinition.Matches(SqlStructureConstants.ENAME_DDL_PARENS)) {
                // if inserting into table, fetch from refcursor using fetch_all_from function and table's column list
                selectionTarget.AddChild(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "fetch_all_from");
                var parens1 = selectionTarget.AddChild(SqlStructureConstants.ENAME_FUNCTION_PARENS, "");
                parens1.AddChild(SqlStructureConstants.ENAME_STRING, $"{name.TextValue}_select1");
                selectionTarget.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "as");
                var tableDefinitionClone = (Node)tableDefinition!.Clone();
                tableDefinitionClone.Name = SqlStructureConstants.ENAME_EXPRESSION_PARENS;
                selectionTarget.AddChild(tableDefinitionClone);
            } else {
                // if inserting into variable of table type, use refcursor_populate_recordset function and table's type
                selectionTarget.AddChild(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "refcursor_populate_recordset");
                var parens2 = selectionTarget.AddChild(SqlStructureConstants.ENAME_FUNCTION_PARENS, "");
                parens2.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "null");
                parens2.AddChild(SqlStructureConstants.ENAME_PERIOD, "::");
                parens2.AddChild(SqlStructureConstants.ENAME_OTHERNODE, tableDefinition.TextValue);
                parens2.AddChild(SqlStructureConstants.ENAME_COMMA, ",");
                parens2.AddChild(SqlStructureConstants.ENAME_STRING, $"{name.TextValue}_select1");
            }
        }
    
        foreach (var child in new List<Node>(element.Children)) {
            ConvertProcedureCalls(child, tempTableDefinitions, declarations);
        }
    }

    private void ConvertOutputParameters(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_DDL_PARENS)) {
            var list = element.Children.ToList();
            var commaIndices = element.ChildrenByName(SqlStructureConstants.ENAME_COMMA).Select(e => list.IndexOf(e)).ToList();
            var outputKeywords = list.FindAll(e => e.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "output"));
            foreach (var output in outputKeywords) {
                var index = list.IndexOf(output);
                var lastCommaIndex = commaIndices.LastOrDefault(e => e < index);
                element.RemoveChild(output);

                if (lastCommaIndex > 0) {
                    var comma = list[lastCommaIndex];
                    element.InsertChildAfter(output, comma);
                } else {
                    element.InsertChildAfter(output, list.First());
                }

                output.TextValue = "out";
            }
        }
    
        foreach (var child in new List<Node>(element.Children)) {
            ConvertOutputParameters(child);
        }
    }

    private void ConvertDelete(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "delete")) {
            var deleteClause = element.Parent;
            var statement = deleteClause.Parent;
            var fromClause = statement.Children.FirstOrDefault(e => e.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "from") != null);
            if (fromClause != null) {
                return;
            }

            var tableName = element.NextNonWsSibling();
            deleteClause.RemoveChild(tableName);

            fromClause = statement.InsertChildAfter(SqlStructureConstants.ENAME_SQL_CLAUSE, "", deleteClause);
            fromClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "from");
            var selectionTarget = fromClause.AddChild(SqlStructureConstants.ENAME_SELECTIONTARGET, "");
            selectionTarget.AddChild(tableName);
        }

        foreach (var child in new List<Node>(element.Children)) {
            ConvertDelete(child);
        }
    }

    private void ConvertOutputClause(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "output")) {
            var outputClause = element.Parent;

            var statement = outputClause.Parent;
            
            var insertClause = statement.Children.FirstOrDefault(c => c.ChildByNameAndText(SqlStructureConstants.ENAME_COMPOUNDKEYWORD, "") != null);
            var updateClause = statement.Children.FirstOrDefault(c => c.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "update") != null);
            var mergeClause = statement.Children.FirstOrDefault(c => c.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "merge") != null);
            var deleteClause = statement.Children.FirstOrDefault(c => c.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "delete") != null);
            var mainClause = insertClause ?? updateClause ?? mergeClause ?? deleteClause;
            
            string cteName;
            if (updateClause != null) cteName = "updated";
            else if (mergeClause != null) cteName = "merged";
            else if (deleteClause != null) cteName = "deleted";
            else cteName = "inserted";
            
            var mergeAction = outputClause.ChildByNameAndText(SqlStructureConstants.ENAME_PSEUDONAME, "$action");
            if (mergeAction != null) {
                mergeAction.Name = SqlStructureConstants.ENAME_FUNCTION_KEYWORD;
                mergeAction.TextValue = "merge_action";
                outputClause.InsertChildAfter(SqlStructureConstants.ENAME_FUNCTION_PARENS, "", mergeAction);
            }

            var outputPeriods = outputClause.ChildrenByName(SqlStructureConstants.ENAME_PERIOD).ToList();
            foreach (var period in outputPeriods) {
                var table = period.PreviousNonWsSibling();

                if (cteName == "inserted" || cteName == "deleted")
                {
                    if (table.TextValue.ToLower() == "inserted" || table.TextValue.ToLower() == "deleted")
                    {
                        outputClause.RemoveChild(table);
                        outputClause.RemoveChild(period);
                    }
                }
                else if (cteName == "updated" || cteName == "merged") {
                    if (table.TextValue.ToLower() == "inserted") {
                        table.TextValue = "new";
                    }
                    else if (table.TextValue.ToLower() == "deleted") {
                        table.TextValue = "old";
                    }
                }
            }
            
            var outputNodes = outputClause.Children.Where(e => e != element).ToList();
            
            var intoClause = outputClause.NextNonWsSibling();
            var intoKeyword = intoClause.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "into")!;
            var outputTableName = intoKeyword.NextNonWsSibling();
            var outputTableColumns = outputTableName.NextNonWsSibling();

            statement.RemoveChild(outputClause);
            statement.RemoveChild(intoClause);
            
            var cteClause = statement.Children.FirstOrDefault(c => c.ChildByName(SqlStructureConstants.ENAME_CTE_WITH_CLAUSE) != null);
            
            if (cteClause == null) {
                cteClause = statement.InsertChildBefore(SqlStructureConstants.ENAME_CTE_WITH_CLAUSE, "", mainClause!);
                var cteClauseOpener = cteClause.AddChild(SqlStructureConstants.ENAME_CONTAINER_OPEN, "");
                cteClauseOpener.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "with");
            } else {
                cteClause = cteClause.ChildByName(SqlStructureConstants.ENAME_CTE_WITH_CLAUSE);
                var cteCommaContainer = cteClause.AddChild(SqlStructureConstants.ENAME_CONTAINER_GENERALCONTENT, "");
                cteCommaContainer.AddChild(SqlStructureConstants.ENAME_COMMA, ",");
            }

            var cteAlias = cteClause.AddChild(SqlStructureConstants.ENAME_CTE_ALIAS, "");
            cteAlias.AddChild(SqlStructureConstants.ENAME_OTHERNODE, cteName);
            var cteAsBlock = cteClause.AddChild(SqlStructureConstants.ENAME_CTE_AS_BLOCK, "");
            var cteAsBlockOpener = cteAsBlock.AddChild(SqlStructureConstants.ENAME_CONTAINER_OPEN, "");
            cteAsBlockOpener.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "as");
            var cteBody = cteAsBlock.AddChild(SqlStructureConstants.ENAME_CONTAINER_GENERALCONTENT, "");
            var cteParens = cteBody.AddChild(SqlStructureConstants.ENAME_SELECTIONTARGET_PARENS, "");
            var mainClauseIndex = statement.Children.ToList().IndexOf(mainClause!);
            var clausesToMove = statement.Children.Skip(mainClauseIndex).ToList();
            foreach (var clause in clausesToMove) {
                clause.Parent.RemoveChild(clause);
                cteParens.AddChild(clause);
            }
            var returningClause = cteParens.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
            returningClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "returning");
            foreach (var node in outputNodes) {
                node.Parent.RemoveChild(node);
                returningClause.AddChild(node);
            }

            var finalInsertClause = statement.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
            var compoundKeyword = finalInsertClause.AddChild(SqlStructureConstants.ENAME_COMPOUNDKEYWORD, "");
            compoundKeyword.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "insert");
            compoundKeyword.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "into");
            compoundKeyword.Attributes["simpleText"] = "insert into";
            outputTableName.Parent.RemoveChild(outputTableName);
            finalInsertClause.AddChild(outputTableName);
            if (outputTableColumns != null) {
                outputTableColumns.Parent.RemoveChild(outputTableColumns);
                finalInsertClause.AddChild(outputTableColumns);
            }

            var finalSelectClause = statement.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
            finalSelectClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "select");
            foreach (var node in outputNodes) {
                finalSelectClause.AddChild((Node)node.Clone());
            }

            var finalFromClause = statement.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
            finalFromClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "from");
            var finalSelectionTarget = finalFromClause.AddChild(SqlStructureConstants.ENAME_SELECTIONTARGET, "");
            finalSelectionTarget.AddChild(SqlStructureConstants.ENAME_OTHERNODE, cteName);
        }
        
        foreach (var child in new List<Node>(element.Children)) {
            ConvertOutputClause(child);
        }
    }

    private void ConvertProcedureSelectsToRefcursors(Node element, ref int selectNumber)
    {
        if (element.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "select")) {
            var procedureBlock = element.Closest(SqlStructureConstants.ENAME_DDL_AS_BLOCK);

            // only relevant in a procedure
            if (procedureBlock == null) {
                return;
            }
                        
            var selectClause = element.Parent;
            var statement = selectClause.Parent;

            //has to be a part of a statement
            if (!statement.Matches(SqlStructureConstants.ENAME_SQL_STATEMENT)) {
                return;
            }

            //has to be the first clause
            if (statement.ChildByNameAndText(SqlStructureConstants.ENAME_SQL_CLAUSE) != selectClause) {
                return;
            }

            //must not have into clause
            if (statement.Children.FirstOrDefault(c => c.ChildByNameAndText(SqlStructureConstants.ENAME_OTHERKEYWORD, "into") != null) != null) {
                return;
            }

            var columns = GetSelectColumns(selectClause);
            var assignmentColumns = columns.FindAll(e =>
                e.FirstOrDefault(e => e.Name == SqlStructureConstants.ENAME_EQUALSSIGN) != null
                && e.First(e => e.IsName()).TextValue.StartsWith("@")
            );

            //must not have any variable assignment
            if (assignmentColumns.Count > 0) {
                return;
            }

            var procedureName = procedureBlock.Parent.ChildByName(SqlStructureConstants.ENAME_DDL_PARENS).PreviousNonWsSibling().TextValue;

            selectNumber = selectNumber + 1;
            var refcursorName = $"_select{selectNumber}";
            var openRefcursorClause = statement.InsertChildBefore(SqlStructureConstants.ENAME_SQL_CLAUSE, "", selectClause);
            openRefcursorClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "open");
            openRefcursorClause.AddChild(SqlStructureConstants.ENAME_OTHERNODE, refcursorName);
            openRefcursorClause.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "for");

            //add an mssql style declare statement for refcursor. It will later be moved into plpgsql declare block along with other declarations
            var declareStatement = statement.Parent.InsertChildBefore(SqlStructureConstants.ENAME_SQL_STATEMENT, "", statement);
            var declareClause = declareStatement.AddChild(SqlStructureConstants.ENAME_SQL_CLAUSE, "");
            var declareBlock = declareClause.AddChild(SqlStructureConstants.ENAME_DDL_DECLARE_BLOCK, "");
            declareBlock.AddChild(SqlStructureConstants.ENAME_OTHERKEYWORD, "declare");
            declareBlock.AddChild(SqlStructureConstants.ENAME_OTHERNODE, refcursorName);
            declareBlock.AddChild(SqlStructureConstants.ENAME_DATATYPE_KEYWORD, "refcursor");
            declareBlock.AddChild(SqlStructureConstants.ENAME_EQUALSSIGN, "");
            declareBlock.AddChild(SqlStructureConstants.ENAME_STRING, $"{procedureName}_{refcursorName}");
        }
    
        foreach (var child in new List<Node>(element.Children))
        {
            ConvertProcedureSelectsToRefcursors(child, ref selectNumber);
        }
    }

    private void ConvertTryCatch(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "try")) {
            var clause = element.Parent;
            var previousWord = element.PreviousNonWsSibling();
            if (previousWord.TextValue.ToLower() == "begin") {
                clause.RemoveChild(element);
                clause.Attributes["simpleText"] = "begin";
            } else {
                var closer = element.Closest(SqlStructureConstants.ENAME_CONTAINER_CLOSE)!;
                closer.Parent.RemoveChild(closer);
            }
        }

        if (element.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "catch")) {
            var clause = element.Parent;
            var previousWord = element.PreviousNonWsSibling();
            if (previousWord.TextValue.ToLower() == "begin") {
                clause.Attributes["simpleText"] = "exception when others then";
            } else {
                clause.Attributes["simpleText"] = "end";
            }
        }
    
        if (element.Matches(SqlStructureConstants.ENAME_OTHERNODE, "throw")) {
            var clause = element.Parent;
            var code = element.NextNonWsSibling();
            var comma = code.NextNonWsSibling();
            var msg = comma.NextNonWsSibling();
            var comma2 = msg.NextNonWsSibling();
            var irrelevant = comma2.NextNonWsSibling();

            clause.RemoveChild(code);
            clause.RemoveChild(comma);
            clause.RemoveChild(msg);
            clause.RemoveChild(comma2);
            clause.RemoveChild(irrelevant);

            element.Name = SqlStructureConstants.ENAME_OTHERKEYWORD;
            element.TextValue = "raise";
            var temp = clause.InsertChildAfter(SqlStructureConstants.ENAME_OTHERKEYWORD, "exception", element);
            clause.InsertChildAfter(msg, temp);
            temp = clause.InsertChildAfter(SqlStructureConstants.ENAME_OTHERKEYWORD, "using", msg);
            temp = clause.InsertChildAfter(SqlStructureConstants.ENAME_OTHERNODE, "errcode", temp);
            temp = clause.InsertChildAfter(SqlStructureConstants.ENAME_EQUALSSIGN, "=", temp);
            temp = clause.InsertChildAfter(SqlStructureConstants.ENAME_STRING, "T0000", temp);
        }

        if (element.Matches(SqlStructureConstants.ENAME_OTHERKEYWORD, "raiserror")) {
            var clause = element.Parent;
            var parens = element.NextNonWsSibling();
            var msg = parens.Children.First(e => e.Name != SqlStructureConstants.ENAME_WHITESPACE);
            clause.RemoveChild(parens);
            element.TextValue = "raise";
            var temp = clause.InsertChildAfter(SqlStructureConstants.ENAME_OTHERKEYWORD, "exception", element);
            clause.InsertChildAfter(msg, temp);
            temp = clause.InsertChildAfter(SqlStructureConstants.ENAME_OTHERKEYWORD, "using", msg);
            temp = clause.InsertChildAfter(SqlStructureConstants.ENAME_OTHERNODE, "errcode", temp);
            temp = clause.InsertChildAfter(SqlStructureConstants.ENAME_EQUALSSIGN, "=", temp);
            temp = clause.InsertChildAfter(SqlStructureConstants.ENAME_STRING, "T0000", temp);
        }

        foreach (var child in new List<Node>(element.Children))
        {
            ConvertTryCatch(child);
        }
    }

    private void ConvertTransactions(Node element) {
        //explicit transactions are removed, they should be handled implicitly with exception handling
        if (element.Matches(SqlStructureConstants.ENAME_BEGIN_TRANSACTION)
            || element.Matches(SqlStructureConstants.ENAME_COMMIT_TRANSACTION)
            || element.Matches(SqlStructureConstants.ENAME_ROLLBACK_TRANSACTION)) {
            var statement = element.Closest(SqlStructureConstants.ENAME_SQL_STATEMENT)!;
            statement.Parent.RemoveChild(statement);
        }

        foreach (var child in new List<Node>(element.Children))
        {
            ConvertTransactions(child);
        }
    }

    private void FixDdlOtherBlockSemicolon(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_DDL_OTHER_BLOCK)) {
            var semicolon = element.ChildByName(SqlStructureConstants.ENAME_SEMICOLON);
            if (semicolon == null) return;

            element.RemoveChild(semicolon);
            element.Parent.AddChild(semicolon);
        }
        
        foreach (var child in new List<Node>(element.Children))
        {
            FixDdlOtherBlockSemicolon(child);
        }
    }

    private void ConvertIdentity(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_FUNCTION_KEYWORD, "identity")) {
            var clause = element.Parent;
            var clauseNodes = clause.Children.ToList();
            var index = clauseNodes.IndexOf(element);
            var commas = clause.ChildrenByName(SqlStructureConstants.ENAME_COMMA);
            var commaIndices = commas.Select(e => clauseNodes.IndexOf(e)).ToList();
            var lastCommaIndexBeforeElement = commaIndices.LastOrDefault(e => e < index, 0);
            var parens = element.NextNonWsSibling();
            clause.RemoveChild(element);
            clause.RemoveChild(parens);
            
            var dataType = clauseNodes.Skip(lastCommaIndexBeforeElement).First(e => e.Matches(SqlStructureConstants.ENAME_DATATYPE_KEYWORD));
            
            if (dataType.TextValue.ToLower() == "int") {
                dataType.TextValue = "serial";
            }
            else if (dataType.TextValue.ToLower() == "bigint") {
                dataType.TextValue = "bigserial";
            }
        }
        
        foreach (var child in new List<Node>(element.Children))
        {
            ConvertIdentity(child);
        }
    }

    private void ConvertNStrings(Node element) {
        if (element.Matches(SqlStructureConstants.ENAME_NSTRING)) {
            element.Name = SqlStructureConstants.ENAME_STRING;
        }

        foreach (var child in element.Children) {
            ConvertNStrings(child);
        }
    }

    public void TransformTree(Node sqlTreeDoc)
    {
        ConvertProceduralBlocks(sqlTreeDoc);
        AddLanguageClause(sqlTreeDoc);
        ForceDdlParens(sqlTreeDoc);
        ForceDdlBeginEnd(sqlTreeDoc);
        AddBlockWrapper(sqlTreeDoc);

        int selectCounter = 0;
        ConvertProcedureSelectsToRefcursors(sqlTreeDoc, ref selectCounter);

        var declarations = FindDeclarations(sqlTreeDoc);
        AddDeclareSection(sqlTreeDoc, declarations);
        var arrayVariables = declarations
                    .FindAll(e => e.variable.Any(e1 => e1.Matches(SqlStructureConstants.ENAME_PERIOD)))
                    .Select(e => e.variable.First(e => e.Matches(SqlStructureConstants.ENAME_OTHERNODE)).TextValue)
                    .ToList();

        arrayVariables?.AddRange(ConvertTableVariables(sqlTreeDoc));
        UnnestArrays(sqlTreeDoc, arrayVariables);

        ConvertDeclareToAssign(sqlTreeDoc);
        ConvertSetToAssign(sqlTreeDoc);
        CleanupDeclareStatements(sqlTreeDoc);
        
        ConvertOldDropTableIfExists(sqlTreeDoc);
        
        ForceIfBeginEnd(sqlTreeDoc);
        ConvertConditions(sqlTreeDoc);

        UpdateSelectStatements(sqlTreeDoc);
        ConvertPivot(sqlTreeDoc);
        ConvertUnpivot(sqlTreeDoc);
        ConvertDelete(sqlTreeDoc);
        var tempTableDefinitions = ConvertTempTables(sqlTreeDoc);
        ConvertLoops(sqlTreeDoc);
        ConvertUpdateFrom(sqlTreeDoc);

        RemoveUnnecessaryStatements(sqlTreeDoc);

        ConvertDirectlyMappedFunctions(sqlTreeDoc);
        ConvertStringAggFunction(sqlTreeDoc);
        ConvertForXmlPathStringAgg(sqlTreeDoc);
        ConvertFormatFunction(sqlTreeDoc);
        ConvertDatePartFunction(sqlTreeDoc);
        ConvertDateAddFunction(sqlTreeDoc);
        ConvertDateDiffFunction(sqlTreeDoc);
        ConvertIifFunction(sqlTreeDoc);
        ConvertStuffFunction(sqlTreeDoc);
        ConvertProcedureCalls(sqlTreeDoc, tempTableDefinitions, declarations);
        InsertIntoArrays(sqlTreeDoc, arrayVariables);
        ConvertTransactions(sqlTreeDoc);
        ConvertTryCatch(sqlTreeDoc);
        ConvertOutputParameters(sqlTreeDoc);
        ConvertJsonFunctions(sqlTreeDoc);
        ConvertForJsonPath(sqlTreeDoc);
        ConvertOutputClause(sqlTreeDoc);

        FixDdlOtherBlockSemicolon(sqlTreeDoc);
        AddMissingSemicolons(sqlTreeDoc);
        ConvertCast(sqlTreeDoc);
        ConvertTryCast(sqlTreeDoc);
        UpdateNames(sqlTreeDoc);
        ConvertDataTypes(sqlTreeDoc);
        ConvertIdentity(sqlTreeDoc);
        ConvertNStrings(sqlTreeDoc);
        FixCommasAfterComments(sqlTreeDoc);
    }
}
