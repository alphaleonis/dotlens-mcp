using TopLevelStatementsTestProject;

Library.Ping();
Func<int> deferred = () => Library.Ping();
var table = args.Length > 0 ? new Table() : null;
_ = table?[0];
Console.WriteLine(deferred() + Local());

int Local() => Library.Ping();
