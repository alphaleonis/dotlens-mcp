using SelfEquatable = System.IEquatable<MemberListsTestProject.AliasedEntry>;

namespace MemberListsTestProject;

public partial record StringEquatableEntry(int Id) : IEquatable<string>
{
   public bool Equals(string? other) => other == Id.ToString();
}

public partial record StringEquatableEntry : IDisposable
{
   public void Dispose() { }
}

public partial record AliasedEntry(int Id) : SelfEquatable;

public partial record AliasedEntry : IDisposable
{
   public void Dispose() { }
}
