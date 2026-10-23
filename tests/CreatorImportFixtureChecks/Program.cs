if (args.Length == 1 && args[0] == "--biceps") BicepsChecks.Run();
else if (args.Length == 2 && args[0] == "--import-values") ImportValueChecks.Run(args[1]);
else throw new ArgumentException("Use --biceps or --import-values <isolated-output-directory>.");
