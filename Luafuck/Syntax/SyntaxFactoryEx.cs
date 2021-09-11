using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Luafuck
{
    public static class SyntaxFactoryEx
    {
        public static AssignmentStatementSyntax AssignmentExpression(PrefixExpressionSyntax leftSide, ExpressionSyntax rightSide)
        {
            var exp = SyntaxFactory.AssignmentStatement(
                SyntaxFactory.SeparatedList<PrefixExpressionSyntax>(
                    new SyntaxNodeOrToken[] { leftSide }),
                SyntaxFactory.SeparatedList<ExpressionSyntax>(
                    new SyntaxNodeOrToken[] { rightSide }));
            return exp;
        }

        public static ExpressionSyntax ConcatStrings(params ExpressionSyntax[] args)
        {
            return ConcatStrings((IList<ExpressionSyntax>)args);
        }
        public static ExpressionSyntax ConcatStrings(IList<ExpressionSyntax> args)
        {
            if (args == null || !args.Any())
            {
                throw new Exception("Empty list of string to concat???");
            }

            int amount = 1 + (args?.Count ?? 0);
            StringBuilder sb = new(amount * 10);
            for (int i = 0; i < args.Count; i++)
            {
                sb.Append(args[i].ToString());
                if(i != args.Count-1)
                {
                    sb.Append("..");
                }
            }    
            return SyntaxFactory.ParseExpression(sb.ToString());
        }

        public static ExpressionSyntax LengthExpression(ExpressionSyntax stringOrTable, bool autoParen = false)
        {
            if (autoParen && stringOrTable is not ParenthesizedExpressionSyntax)
            {
                stringOrTable = stringOrTable.Parenthesize();
            }
            var token = SyntaxFactory.ParseToken("#");
            return SyntaxFactory.UnaryExpression(SyntaxKind.LengthExpression, token, stringOrTable);
        }

        public static FunctionCallExpressionSyntax FuncCall(PrefixExpressionSyntax functionExpression, params ExpressionSyntax[] args)
        {
            // Edge case: We can get rid of the invocation parenthesis if we have exactly 1 args and it's a literal string
            if(args.Length == 1 && args[0] is LiteralExpressionSyntax lit)
            {
                return SyntaxFactory.FunctionCallExpression(functionExpression,
                                                    SyntaxFactory.StringFunctionArgument(lit));
            }

            return SyntaxFactory.FunctionCallExpression(functionExpression,
                                                        SyntaxFactory.ExpressionListFunctionArgument(
                                                                            SyntaxFactory.SeparatedList(args)));
        }

        public static SyntaxTree SyntaxTree(IEnumerable<StatementSyntax> Nodes)
        {
            return SyntaxFactory.SyntaxTree(
                        SyntaxFactory.CompilationUnit(
                            SyntaxFactory.StatementList(Nodes)));
        }

        public static ParenthesizedExpressionSyntax Parenthesize(this ExpressionSyntax exp)
        {
            return SyntaxFactory.ParenthesizedExpression(exp);

        }

        public static string ExtractString(this LiteralExpressionSyntax literalStringExpression)
        {
            if (literalStringExpression.Kind() != SyntaxKind.StringLiteralExpression)
            {
                throw new ArgumentException("Must be a string literal");
            }

            return literalStringExpression.Token.ValueText;
        }

    }
}
