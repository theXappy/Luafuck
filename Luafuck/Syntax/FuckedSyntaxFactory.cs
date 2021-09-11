using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using System;

namespace Luafuck
{
    public class FuckedSyntaxFactory
    {
        public static ElementAccessExpressionSyntax FuckedVariableAccessExpression(string globalTableVariable, string varName)
        {
            var elementAccess = SyntaxFactory.ElementAccessExpression(
                SyntaxFactory.IdentifierName(globalTableVariable),
                SyntaxFactory.ParenthesizedExpression(
                    SyntaxFactory.ParseExpression($"[[{varName}]]")));
            return elementAccess;
        }

        public static LiteralExpressionSyntax FuckedEmptyString()
        {
            return SyntaxFactory.ParseExpression("[[]]") as LiteralExpressionSyntax;
        }

        internal static ExpressionSyntax FuckedZeroInteger(string globalTableVariable)
        {
            UnaryExpressionSyntax x = SyntaxFactory.ParseExpression($"#{globalTableVariable}") as UnaryExpressionSyntax;
            return x;
        }
        public static ExpressionSyntax DoubleBracketsString(string str = "")
        {
            return SyntaxFactory.ParseExpression("[["+str+"]]");
        }


    }
}
