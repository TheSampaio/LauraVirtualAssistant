using System.Globalization;

// Only the compiled binary and sampled outputs are disclosed to the model under test.
if (args.Length != 1 || !int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out int input) || input is < 0 or > 100)
{ Console.Error.WriteLine("Expected one integer between 0 and 100."); return 2; }
Console.WriteLine(((7 * input * input + 13 * input + 29) % 101).ToString(CultureInfo.InvariantCulture));
return 0;
