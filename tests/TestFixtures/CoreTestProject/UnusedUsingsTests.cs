// This file is used for testing organize_usings tool
// It intentionally has unused and unsorted usings

using System.Text.RegularExpressions;  // Unused
using System.Collections.Generic;
using CoreTestProject.Types;
using System;
using System.Linq;  // Unused
using System.Threading.Tasks;  // Unused

namespace CoreTestProject;

/// <summary>
/// Test class for organize usings tests.
/// Only uses System, System.Collections.Generic, and CoreTestProject.Types
/// </summary>
public class UnusedUsingsTestClass
{
   private readonly List<string> _items = new();

   public void DoSomething()
   {
      Console.WriteLine("Hello");
      _items.Add("test");
   }

   public SampleClass CreateSample()
   {
      return new SampleClass();
   }
}
