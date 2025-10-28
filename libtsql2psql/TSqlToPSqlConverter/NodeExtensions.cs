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

using System.Text;
using PoorMansTSqlFormatterLib.Interfaces;
using PoorMansTSqlFormatterLib.ParseStructure;

namespace TSqlToPSql;

public static class ConverterNodeExtensions {
    public static Node? FindSemicolon(this Node node) {
        if (node.Children.Count() == 0) return null;
        var semicolon = node.ChildByName(SqlStructureConstants.ENAME_SEMICOLON);
        if (semicolon != null) return semicolon;

        var lastChild = node.Children.LastOrDefault(e => e.Name != SqlStructureConstants.ENAME_WHITESPACE);
        if (lastChild == null) return null;
        return lastChild.FindSemicolon();
    }

    public static bool EndsWithSemicolon(this Node node)
    {
        var semicolon = node.FindSemicolon();
        return semicolon != null;
    }
	
    public static Node AddChild(this Node node, string name, string text) {
        var child = new NodeImpl
        {
            Parent = node,
            Name = name,
            TextValue = text
        };
        ((IList<Node>)node.Children).Add(child);
        return child;
    }

    public static Node InsertChildBefore(this Node node, string name, string text, Node existingChild) {
        var childList = node.Children as IList<Node>;
        var child = new NodeImpl
        {
            Parent = node,
            Name = name,
            TextValue = text
        };
        childList.Insert(childList.IndexOf(existingChild), child);
        return child;
    }

    public static Node InsertChildAfter(this Node node, string name, string text, Node existingChild) {
        var childList = node.Children as IList<Node>;
        var child = new NodeImpl
        {
            Parent = node,
            Name = name,
            TextValue = text
        };
        childList.Insert(childList.IndexOf(existingChild) + 1, child);
        return child;
    }

    public static void InsertChildAfter(this Node node, Node newChild, Node existingChild)
    {
		newChild.Parent = node;
        var childList = node.Children as IList<Node>;
        childList.Insert(childList.IndexOf(existingChild) + 1, newChild);
    }

    public static string DumpTree(this Node node, bool ignoreWhitespace = true, StringBuilder? sb = null, int indentation = 0, bool lastChild = false, int[]? indentLevelsOver = null) {
        if (sb == null)
        {
            sb = new StringBuilder();
        }

        if (indentLevelsOver == null)
        {
            indentLevelsOver = [];
        }

        if (ignoreWhitespace && node.Name == SqlStructureConstants.ENAME_WHITESPACE)
        {
            return "";
        }

        for (var i = 0; i < indentation - 1; i++)
        {
            sb.Append(indentLevelsOver.Contains(i) ? "  " : "│ ");
        }

        if (indentation > 0)
        {
            sb.Append(lastChild ? "└─" : "├─");
        }

        if (lastChild || indentation == 0)
        {
            indentLevelsOver = indentLevelsOver.Concat([indentation - 1]).ToArray();
        }

        sb.Append($"{(string.IsNullOrWhiteSpace(node.TextValue) ? "" : $"{node.TextValue.Replace("\n", "\\n").Replace("\t", "\\t")} / ")}{node.Name}\n");

        var _lastChild = node.Children.LastOrDefault(e => ignoreWhitespace ? e.Name != SqlStructureConstants.ENAME_WHITESPACE : true);
        foreach (var child in node.Children)
        {
            child.DumpTree(ignoreWhitespace, sb, indentation + 1, child == _lastChild, indentLevelsOver);
        }

        if (indentation == 0)
        {
            return sb.ToString();
        }

        return "";
    }

    public static Node? ChildByNameAndText(this Node node, string name, string? text = null)
    {
        return node.Children.FirstOrDefault(e => e.Name == name && (text == null || e.TextValue.ToLower() == text));
    }
    
    public static Node? LastChildByNameAndText(this Node node, string name, string? text = null) {
        return node.Children.LastOrDefault(e => e.Name == name && (text == null || e.TextValue.ToLower() == text));
    }

    public static bool Matches(this Node node, string name, string? text = null) {
        return node.Name == name && (text == null || node.TextValue.ToLower() == text);
    }

    /*
        these are generally dangerous to use cause it's easy to return something unrelated far down the tree, or just do unnecessary traversal
        better to explicitly match everything
        but can still be useful sometimes, it's usually safe to return closest clause or statement
    */
    public static Node? Closest(this Node node, string name) {
        if (node.Parent == null) return null;
        if (node.Parent.Name == name) return node.Parent;
        return node.Parent.Closest(name);
    }

    public static Node? ClosestChild(this Node node, string name, string? text = null) {
        var result = node.ChildByNameAndText(name, text);
        if (result != null) return result;

        return node.Children.FirstOrDefault()?.ChildByNameAndText(name, text);
    }

    public static string ToSnakeCase(this string str)
    {
        return string.Concat(
        str.Select((x, i) =>
            i > 0 && char.IsUpper(x) && (char.IsLower(str[i - 1]) || i < str.Length - 1 && char.IsLower(str[i + 1]))
                ? "_" + x
                : x.ToString())).ToLowerInvariant();
    }

    public static bool IsComment(this Node node)
    {
        return node.Name == SqlStructureConstants.ENAME_COMMENT_MULTILINE || node.Name == SqlStructureConstants.ENAME_COMMENT_SINGLELINE;
    }

    public static string[] KeywordsAcceptableAsColumnNames = [
        "value",
        "text",
        "status",
        "str",
        "datetime",
        "date",
        "query",
        "language"
    ];

    public static bool IsName(this Node node)
    {
        return node.Name == SqlStructureConstants.ENAME_OTHERNODE
            || node.Name == SqlStructureConstants.ENAME_BRACKET_QUOTED_NAME
            || KeywordsAcceptableAsColumnNames.Contains(node.TextValue?.ToLower());
    }

    public static Node FollowingNonWsChild(this Node value, Node fromChild, bool allowComments = false)
    {
        if (value == null)
            return null;

        if (fromChild == null)
            throw new ArgumentNullException("fromChild");

        bool targetFound = false;
        Node sibling = null;

        foreach (var child in value.Children)
        {
            if (child.Matches(SqlStructureConstants.ENAME_WHITESPACE)) continue;

            if (!allowComments)
            {
                if (child.IsComment()) continue;
            }

            if (targetFound)
            {
                sibling = child;
                break;
            }

            if (child == fromChild)
                targetFound = true;
        }

        return sibling;
    }

    public static Node PreviousNonWsChild(this Node value, Node fromChild)
    {
        if (value == null)
            return null;

        if (fromChild == null)
            throw new ArgumentNullException("fromChild");

        Node previousSibling = null;

        foreach (var child in value.Children)
        {
            if (child.Matches(SqlStructureConstants.ENAME_WHITESPACE)) continue;
            if (child.IsComment()) continue;

            if (child == fromChild)
                return previousSibling;

            previousSibling = child;
        }

        return null;
    }

    /// <summary>
    /// Next non-whitespace sibling
    /// </summary>
    public static Node NextNonWsSibling(this Node value, bool allowComments = false)
    {
        if (value == null || value.Parent == null)
            return null;

        return value.Parent.FollowingNonWsChild(value, allowComments);
    }

    /// <summary>
    /// Previous non-whitespace sibling
    /// </summary>
    public static Node PreviousNonWsSibling(this Node value)
    {
        if (value == null || value.Parent == null)
            return null;

        return value.Parent.PreviousNonWsChild(value);
    }
}
