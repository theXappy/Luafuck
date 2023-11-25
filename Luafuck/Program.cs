using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Luafuck
{
    class Program
    {
        static void Main(string[] args)
        {
            string originalFilePath = args.LastOrDefault();
            if(originalFilePath == null || args.Contains("-h"))
            {
                Console.WriteLine($"Usage: {System.AppDomain.CurrentDomain.FriendlyName} [-l] <input_lua_script_path>\n -l   Legacy mode. use 'loadstring' instead of 'load'.");
                return;
            }
            if(!File.Exists(originalFilePath))
            {
                Console.WriteLine($"No such file '{originalFilePath}'");
                return;
            }

            bool legacyMode = args.Contains("-l");


            var originalCode  = File.ReadAllText(originalFilePath);
            SyntaxTree tree = LuaSyntaxTree.ParseText(originalCode);

            // Good for debugging:
            //
            //StatementListSyntax topLevelStatementsSyntax = root.ChildNodes().Single() as StatementListSyntax;
            //List<SyntaxNode> topLevelStatmentsList = topLevelStatementsSyntax.ChildNodes().ToList();
            //

            ScriptObfuscator so = new();
            SyntaxTree obfusTree = so.Obfuscate(tree, legacyMode);

            Console.Write(obfusTree.ToString());
        }
    }
}
