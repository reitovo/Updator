using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

var configPath = "./sources.json";
if (!File.Exists(configPath)) {
   AnsiConsole.MarkupLine("[underline red]Hello[/] World!");
   return;
}