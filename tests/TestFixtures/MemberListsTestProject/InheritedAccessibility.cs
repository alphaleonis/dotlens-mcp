using MemberListsBaseLibrary;
using MemberListsFriendLibrary;

namespace MemberListsTestProject;

public class Account : Ledger
{
   public void Deposit() { }
}

public class Diary : Journal
{
   public void Read() { }
}

public class Vault
{
   private int _code = 7;

   public int Code => _code;

   public class Inner : Vault;
}
