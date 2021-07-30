using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using System;
using System.Collections.Generic;

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

        public static ExpressionSyntax ConcatStrings(ExpressionSyntax first, ExpressionSyntax second)
        {
            return SyntaxFactory.ParseExpression(first.ToFullString() + ".." + second.ToFullString());
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
            if(literalStringExpression.Kind() != SyntaxKind.StringLiteralExpression)
            {
                throw new ArgumentException("Must be a string literal");
            }

            return literalStringExpression.Token.ValueText;
        }

    }
}
