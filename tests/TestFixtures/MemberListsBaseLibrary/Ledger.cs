namespace MemberListsBaseLibrary;

public class Ledger
{
   public void Post() { }

   protected void Audit() { }

   internal void Reconcile() { }

   protected internal void Close() { }

   private protected void Seal() { }

   private void Shred() { }

   internal int Pending = 1;

   internal class Draft;
}
