using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;

namespace SDKGeneratorBNM
{
    public class CodeWriter
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private int _indentLevel;
        private string _indentString = "    ";
        public HashSet<TypeDefinition> Imports = new HashSet<TypeDefinition>(new TypeDefComparer());
        public string CurrentNamespace = "";

        public void Indent() => _indentLevel++;

        public void Unindent()
        {
            if (_indentLevel > 0)
                _indentLevel--;
        }

        public void Line(string text = "")
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _sb.AppendLine();
                return;
            }
            for (int i = 0; i < _indentLevel; i++)
                _sb.Append(_indentString);
            _sb.AppendLine(text);
        }

        public void Raw(string text) => _sb.Append(text);

        public void StartNamespace(string ns)
        {
            CurrentNamespace = string.IsNullOrEmpty(ns) ? "GlobalNamespace" : ns;
            Line($"namespace {Utils.FixNamespace(CurrentNamespace)} {{");
            Indent();
        }

        public void EndNamespace()
        {
            Unindent();
            Line("}");
        }

        public void Save(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            var header = new StringBuilder();
            header.AppendLine("#pragma once");
            header.AppendLine("#include <BNMIncludes.hpp>");
            if (Config.UseBNMResolve)
                header.AppendLine("#include <BNMResolve.hpp>");

            bool isForwardDec = path.EndsWith("ForwardDeclarations.hpp");
            if (!isForwardDec)
            {
                header.AppendLine("#include \"../ForwardDeclarations.hpp\"");
            }

            var sortedImports = Imports
                .Where(i => i != null)
                .Select(i => new
                {
                    Namespace = Utils.GetNamespace(i),
                    Name = Utils.FormatTypeNameForStruct(i),
                    Type = i,
                })
                .GroupBy(i => i.Namespace + "." + i.Name)
                .Select(g => g.First())
                .OrderBy(i => i.Namespace)
                .ThenBy(i => i.Name);

            string currentFileName = Path.GetFileName(path);
            foreach (var import in sortedImports)
            {
                string importFileName = import.Name + ".hpp";
                if (import.Namespace == CurrentNamespace && importFileName == currentFileName)
                    continue;
                string relPath = Utils.GetRelativeIncludePath(CurrentNamespace, import.Namespace, import.Name);
                header.AppendLine($"#include \"{relPath}\"");
            }
            header.AppendLine();
            string content = header.ToString() + _sb.ToString().Replace("StringComparison", "int");
            string saveDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(saveDir))
                Directory.CreateDirectory(saveDir);
            File.WriteAllText(path, content);
        }

        public override string ToString() => _sb.ToString();
    }
}
