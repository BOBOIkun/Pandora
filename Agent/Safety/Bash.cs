using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Text.Json;
using Pandora.Models;
namespace Pandora.Agent.Safety
{
    public static class BashCommand
    {
        public readonly static HashSet<string> allowCommands = [];
        static BashCommand()
        {
            string path1 = Path.Combine(AppContext.BaseDirectory, "config", "bash.json");
            string path2 = Path.Combine(AppContext.BaseDirectory, "config", "aliases.txt");
            Dictionary<string, List<string>> aliases = [];
            foreach (string cmd in File.ReadLines(path2)) 
            {
                string[] strings = cmd.Split(',');
                aliases.TryAdd(strings[1], []);
                aliases[strings[1]].Add(strings[0]);
            }
            string json = File.ReadAllText(path1);
            using var doc = JsonDocument.Parse(json);
            var allow = doc.RootElement.GetProperty("allow");
            foreach (var cmd in allow.EnumerateArray())
            {
                string? c= cmd.GetString();
                if (string.IsNullOrEmpty(c) || !aliases.ContainsKey(c))
                {
                    continue;
                }
                allowCommands.Add(c);
                foreach (string n in aliases[c])
                {
                    allowCommands.Add(n);
                }
            }
        }
        public static BashCommandInfo GetBashCommandInfo(string command)
        {
            List<string> commands = [];
            List<string> errorMessages = [];
            var commandInfo = new BashCommandInfo();
            var ast = Parser.ParseInput(command, out _, out ParseError[] e);
            foreach (ParseError error in e)
            {
                errorMessages.Add(error.Message);
            }
            var commands_ = ast.FindAll(n => n is CommandAst, true);

            foreach (CommandAst cmd in commands_.Cast<CommandAst>())
            {
                var name = cmd.GetCommandName();
                commands.Add(name);
            }
            commandInfo.commands = commands;
            commandInfo.errorMessages = errorMessages;
            return commandInfo;
        }
    }
}
