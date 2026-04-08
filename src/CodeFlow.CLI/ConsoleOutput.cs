using System;
using System.Collections.Generic;
using Spectre.Console;

namespace CodeFlow.CLI
{
    /// <summary>Rich terminal output helpers built on Spectre.Console.</summary>
    public static class Out
    {
        public static void Success(string msg) => AnsiConsole.MarkupLine($"[green]✔[/] {Escape(msg)}");
        public static void Error(string msg) => AnsiConsole.MarkupLine($"[red]✗[/] {Escape(msg)}");
        public static void Warn(string msg) => AnsiConsole.MarkupLine($"[yellow]⚠[/] {Escape(msg)}");
        public static void Info(string msg) => AnsiConsole.MarkupLine($"[cyan]ℹ[/] {Escape(msg)}");
        public static void Dim(string msg) => AnsiConsole.MarkupLine($"[grey]{Escape(msg)}[/]");
        public static void Header(string msg) => AnsiConsole.MarkupLine($"\n[bold cyan]{Escape(msg)}[/]");
        public static void Rule(string title = "") => AnsiConsole.Write(new Rule(title).RuleStyle("grey"));

        public static void CommitLine(string hash, string message, string author, string time, bool isHead = false)
        {
            var marker = isHead ? "[bold yellow]* (HEAD)[/] " : "[green]*[/] ";
            AnsiConsole.MarkupLine($"{marker}[yellow]{hash[..8]}[/] {Escape(message)} [grey]— {Escape(author)} {time}[/]");
        }

        public static void DiffAdded(string line) => AnsiConsole.MarkupLine($"[green]+{Escape(line)}[/]");
        public static void DiffRemoved(string line) => AnsiConsole.MarkupLine($"[red]-{Escape(line)}[/]");
        public static void DiffContext(string line) => AnsiConsole.MarkupLine($"[grey] {Escape(line)}[/]");
        public static void DiffHeader(string line) => AnsiConsole.MarkupLine($"[cyan]{Escape(line)}[/]");

        public static void Table(string title, IEnumerable<string[]> rows, params string[] headers)
        {
            var t = new Table().Title(title).Border(TableBorder.Rounded).BorderColor(Color.Grey);
            foreach (var h in headers) t.AddColumn(new TableColumn($"[bold]{h}[/]"));
            foreach (var row in rows)
            {
                t.AddRow(row);
            }
            AnsiConsole.Write(t);
        }

        public static void Progress(string title, Action<ProgressTask> work)
        {
            AnsiConsole.Progress()
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn()
                })
                .Start(ctx =>
                {
                    var task = ctx.AddTask(title);
                    work(task);
                });
        }

        public static void Spinner(string title, Action work)
        {
            AnsiConsole.Status()
                .Spinner(Spectre.Console.Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .Start(title, _ => work());
        }

        private static string Escape(string s) => s
            .Replace("[", "[[")
            .Replace("]", "]]");
    }
}
