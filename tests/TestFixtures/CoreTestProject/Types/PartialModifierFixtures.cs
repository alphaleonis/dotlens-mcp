namespace CoreTestProject.Types;

/// <summary>
/// A partial class that exists only in this single file.
/// Used to test that partial detection checks the keyword, not declaration count.
/// </summary>
public partial class SingleFilePartialClass
{
   public void DoWork() { }
}

/// <summary>
/// A non-partial class for comparison - should NOT match partial filter.
/// </summary>
public class NonPartialClass
{
   public void DoWork() { }
}
