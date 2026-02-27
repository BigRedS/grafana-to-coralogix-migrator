using GrafanaToCx.Cli.Cli;

var exitCode = await AppRunner.RunAsync(args);
Environment.Exit(exitCode);
