using System.Collections.Frozen;

namespace DotLens.Tools.Analysis;

/// <summary>
/// Recognizes the members that a framework calls through reflection and the types that it creates, which source code
/// need not reference.
/// </summary>
internal sealed class FrameworkEntryPoints
{
   private static readonly FrozenSet<string> BuiltInAttributes = FrozenSet.Create(
      StringComparer.Ordinal,
      // Test frameworks
      "Xunit.FactAttribute",
      "Xunit.TheoryAttribute",
      "NUnit.Framework.TestAttribute",
      "NUnit.Framework.TestCaseAttribute",
      "NUnit.Framework.TestCaseSourceAttribute",
      "NUnit.Framework.TheoryAttribute",
      "NUnit.Framework.SetUpAttribute",
      "NUnit.Framework.TearDownAttribute",
      "NUnit.Framework.OneTimeSetUpAttribute",
      "NUnit.Framework.OneTimeTearDownAttribute",
      "NUnit.Framework.TestFixtureAttribute",
      "NUnit.Framework.SetUpFixtureAttribute",
      "Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute",
      "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute",
      "Microsoft.VisualStudio.TestTools.UnitTesting.TestInitializeAttribute",
      "Microsoft.VisualStudio.TestTools.UnitTesting.TestCleanupAttribute",
      "Microsoft.VisualStudio.TestTools.UnitTesting.ClassInitializeAttribute",
      "Microsoft.VisualStudio.TestTools.UnitTesting.ClassCleanupAttribute",
      "Microsoft.VisualStudio.TestTools.UnitTesting.AssemblyInitializeAttribute",
      "Microsoft.VisualStudio.TestTools.UnitTesting.AssemblyCleanupAttribute",
      // Benchmarks
      "BenchmarkDotNet.Attributes.BenchmarkAttribute",
      "BenchmarkDotNet.Attributes.GlobalSetupAttribute",
      "BenchmarkDotNet.Attributes.GlobalCleanupAttribute",
      "BenchmarkDotNet.Attributes.IterationSetupAttribute",
      "BenchmarkDotNet.Attributes.IterationCleanupAttribute",
      "BenchmarkDotNet.Attributes.ParamsAttribute",
      "BenchmarkDotNet.Attributes.ParamsAllValuesAttribute",
      // Serializers
      "System.Text.Json.Serialization.JsonConstructorAttribute",
      "System.Text.Json.Serialization.JsonIncludeAttribute",
      "System.Text.Json.Serialization.JsonPropertyNameAttribute",
      "System.Text.Json.Serialization.JsonExtensionDataAttribute",
      "Newtonsoft.Json.JsonConstructorAttribute",
      "Newtonsoft.Json.JsonPropertyAttribute",
      "Newtonsoft.Json.JsonExtensionDataAttribute",
      "System.Runtime.Serialization.DataMemberAttribute",
      "System.Runtime.Serialization.OnDeserializedAttribute",
      "System.Runtime.Serialization.OnDeserializingAttribute",
      "System.Runtime.Serialization.OnSerializedAttribute",
      "System.Runtime.Serialization.OnSerializingAttribute",
      // Composition
      "System.Composition.ExportAttribute",
      "System.Composition.ImportAttribute",
      "System.Composition.ImportManyAttribute",
      "System.Composition.ImportingConstructorAttribute",
      "System.Composition.OnImportsSatisfiedAttribute",
      "System.ComponentModel.Composition.ExportAttribute",
      "System.ComponentModel.Composition.ImportAttribute",
      "System.ComponentModel.Composition.ImportManyAttribute",
      "System.ComponentModel.Composition.ImportingConstructorAttribute",
      // Blazor
      "Microsoft.AspNetCore.Components.ParameterAttribute",
      "Microsoft.AspNetCore.Components.CascadingParameterAttribute",
      "Microsoft.AspNetCore.Components.InjectAttribute",
      "Microsoft.AspNetCore.Components.SupplyParameterFromQueryAttribute",
      "Microsoft.AspNetCore.Components.SupplyParameterFromFormAttribute",
      "Microsoft.JSInterop.JSInvokableAttribute",
      // Native interop
      "System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute");

   private const string MvcAssemblyName = "Microsoft.AspNetCore.Mvc.Core";
   private const string ControllerBaseName = "Microsoft.AspNetCore.Mvc.ControllerBase";
   private const string ControllerAttributeName = "Microsoft.AspNetCore.Mvc.ControllerAttribute";
   private const string NonControllerAttributeName = "Microsoft.AspNetCore.Mvc.NonControllerAttribute";
   private const string NonActionAttributeName = "Microsoft.AspNetCore.Mvc.NonActionAttribute";
   private const string PageModelName = "Microsoft.AspNetCore.Mvc.RazorPages.PageModel";
   private const string NonHandlerAttributeName = "Microsoft.AspNetCore.Mvc.RazorPages.NonHandlerAttribute";
   private const string HubName = "Microsoft.AspNetCore.SignalR.Hub";
   private const string HttpContextName = "Microsoft.AspNetCore.Http.HttpContext";
   private const string ApplicationBuilderName = "Microsoft.AspNetCore.Builder.IApplicationBuilder";
   private const string ServiceCollectionName = "Microsoft.Extensions.DependencyInjection.IServiceCollection";

   /// <summary>
   /// The attributes whose string argument names a member that supplies test or benchmark data.
   /// </summary>
   private static readonly FrozenSet<string> DataSourceAttributes = FrozenSet.Create(
      StringComparer.Ordinal,
      "Xunit.MemberDataAttribute",
      "NUnit.Framework.TestCaseSourceAttribute",
      "NUnit.Framework.TestFixtureSourceAttribute",
      "NUnit.Framework.ValueSourceAttribute",
      "Microsoft.VisualStudio.TestTools.UnitTesting.DynamicDataAttribute",
      "BenchmarkDotNet.Attributes.ParamsSourceAttribute",
      "BenchmarkDotNet.Attributes.ArgumentsSourceAttribute");

   private readonly string[] _attributeNames;

   /// <param name="attributeNames">
   /// More attributes that mark an entry point, each the full name or the simple name, with or without the
   /// <c>Attribute</c> suffix. A simple name matches in any letter case.
   /// </param>
   public FrameworkEntryPoints(IEnumerable<string>? attributeNames)
   {
      _attributeNames = attributeNames?
         .Where(name => !string.IsNullOrWhiteSpace(name))
         .Select(name => name.Trim())
         .ToArray() ?? [];
   }

   /// <summary>
   /// Returns whether a framework calls the method or constructor, or sets the field or property.
   /// </summary>
   public bool IsCalled(ISymbol member) =>
      HasEntryPointAttribute(member)
      || (member is IMethodSymbol method && (IsAction(method) || IsPageHandler(method) || IsHubMethod(method)));

   /// <summary>
   /// Returns whether a framework creates instances of the type, so it calls the type's instance constructors.
   /// </summary>
   public bool CreatesInstances(INamedTypeSymbol type) =>
      HasEntryPointAttribute(type)
      || IsController(type)
      || DerivesFrom(type, PageModelName)
      || DerivesFrom(type, HubName)
      || type.GetMembers().Any(member =>
         !member.IsStatic && member is not IMethodSymbol { MethodKind: MethodKind.Constructor } && IsCalled(member));

   /// <summary>
   /// Returns whether a framework calls the member once code hands it the member's type, as a type argument or in
   /// <c>typeof</c>: an instance constructor, an ASP.NET Core middleware's <c>Invoke</c> or <c>InvokeAsync</c>, or a
   /// startup class's <c>Configure</c> methods.
   /// </summary>
   /// <remarks>
   /// The middleware method follows <see href="https://github.com/dotnet/aspnetcore/blob/3a3c6d0b75c1f26c3a4c15cd2b786fb9929c4136/src/Http/Http.Abstractions/src/Extensions/UseMiddlewareExtensions.cs#L67-L95">UseMiddleware</see>:
   /// a public instance method that returns a task and takes an <c>HttpContext</c> first. The startup methods follow
   /// <see href="https://github.com/dotnet/aspnetcore/blob/3a3c6d0b75c1f26c3a4c15cd2b786fb9929c4136/src/Hosting/Hosting/src/Internal/StartupLoader.cs#L296-L334">StartupLoader</see>,
   /// which matches the names in any letter case.
   /// </remarks>
   public static bool IsCalledThroughType(ISymbol member) => member switch
   {
      IMethodSymbol { MethodKind: MethodKind.Constructor, IsStatic: false, ContainingType: { IsAbstract: false } } => true,
      IMethodSymbol { MethodKind: MethodKind.Ordinary, DeclaredAccessibility: Accessibility.Public } method =>
         IsMiddlewareInvoke(method) || IsStartupMethod(method),
      _ => false,
   };

   private static bool IsMiddlewareInvoke(IMethodSymbol method) =>
      method is { Name: "Invoke" or "InvokeAsync", IsStatic: false, Parameters: [{ Type: var first }, ..] }
      && first.ToDisplayString() == HttpContextName
      && SelfAndBaseTypes(method.ReturnType as INamedTypeSymbol).Any(type => type.ToDisplayString() == "System.Threading.Tasks.Task");

   private static bool IsStartupMethod(IMethodSymbol method) =>
      method.Name.StartsWith("Configure", StringComparison.OrdinalIgnoreCase)
      && (method.Name.EndsWith("Container", StringComparison.OrdinalIgnoreCase)
         || method.Parameters.Any(parameter => parameter.Type.ToDisplayString() is ApplicationBuilderName or ServiceCollectionName));

   /// <summary>
   /// Returns the members that a data source attribute names in its string argument.
   /// </summary>
   /// <param name="attribute">An attribute, of any class.</param>
   /// <param name="owner">The symbol that the attribute is on, or the method of the parameter that it is on.</param>
   /// <returns>
   /// The members of that name in the type the attribute gives, by its <c>MemberType</c> or a <c>typeof</c> argument,
   /// or else in the owner's type, base types included. The list is empty for any other attribute.
   /// </returns>
   public static IEnumerable<ISymbol> GetNamedDataSources(AttributeData attribute, ISymbol owner)
   {
      if (!SelfAndBaseTypes(attribute.AttributeClass).Any(current => DataSourceAttributes.Contains(current.ToDisplayString()))
         || attribute.ConstructorArguments.FirstOrDefault(argument => argument is { Kind: TypedConstantKind.Primitive, Value: string }).Value is not string name)
      {
         return [];
      }

      var declaringType = attribute.NamedArguments.FirstOrDefault(argument => argument.Key == "MemberType").Value.Value as INamedTypeSymbol
         ?? attribute.ConstructorArguments.FirstOrDefault(argument => argument.Kind == TypedConstantKind.Type).Value as INamedTypeSymbol
         ?? owner as INamedTypeSymbol
         ?? owner.ContainingType;
      return SelfAndBaseTypes(declaringType?.OriginalDefinition)
         .SelectMany(type => type.GetMembers(name))
         .Where(member => member is not INamedTypeSymbol);
   }

   /// <summary>
   /// Returns whether the symbol has an attribute from the built-in list or from the caller's names, or one that derives
   /// from such an attribute.
   /// </summary>
   private bool HasEntryPointAttribute(ISymbol symbol) =>
      symbol.GetAttributes().Any(attribute => SelfAndBaseTypes(attribute.AttributeClass).Any(IsEntryPointAttribute));

   private bool IsEntryPointAttribute(INamedTypeSymbol attributeClass)
   {
      var fullName = attributeClass.OriginalDefinition.ToDisplayString();
      return BuiltInAttributes.Contains(fullName)
         || _attributeNames.Any(name =>
            fullName == name
            || fullName == $"{name}Attribute"
            || attributeClass.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            || attributeClass.Name.Equals($"{name}Attribute", StringComparison.OrdinalIgnoreCase));
   }

   /// <summary>
   /// Returns whether ASP.NET Core MVC can route requests to the method as an action.
   /// </summary>
   /// <remarks>
   /// It follows <see href="https://github.com/dotnet/aspnetcore/blob/3a3c6d0b75c1f26c3a4c15cd2b786fb9929c4136/src/Mvc/Mvc.Core/src/ApplicationModels/DefaultApplicationModelProvider.cs#L410-L460">IsAction</see>.
   /// MVC reads the actions from each public method of a controller, inherited ones included, see
   /// <see href="https://github.com/dotnet/aspnetcore/blob/3a3c6d0b75c1f26c3a4c15cd2b786fb9929c4136/src/Mvc/Mvc.Core/src/ApplicationModels/DefaultApplicationModelProvider.cs#L74">CreateControllerModel</see>.
   /// So a method of an abstract class that derives from <c>ControllerBase</c> counts too.
   /// </remarks>
   private static bool IsAction(IMethodSymbol method) =>
      method is { MethodKind: MethodKind.Ordinary, DeclaredAccessibility: Accessibility.Public, IsStatic: false, IsAbstract: false, IsGenericMethod: false }
      && method.ContainingType is { } type
      && (IsController(type) || DerivesFrom(type, ControllerBaseName))
      && !SelfAndOverridden(method).Any(overridden => HasAttribute(overridden, NonActionAttributeName))
      && BaseDefinition(method).ContainingType.SpecialType != SpecialType.System_Object
      && !IsDisposeMethod(method);

   /// <summary>
   /// Returns whether ASP.NET Core MVC treats the type as a controller.
   /// </summary>
   /// <remarks>
   /// It follows <see href="https://github.com/dotnet/aspnetcore/blob/3a3c6d0b75c1f26c3a4c15cd2b786fb9929c4136/src/Mvc/Mvc.Core/src/Controllers/ControllerFeatureProvider.cs#L39-L75">IsController</see>,
   /// which reads the attributes of base classes too. It needs the project to reference MVC, as a class named with
   /// the <c>Controller</c> suffix is no controller elsewhere.
   /// </remarks>
   private static bool IsController(INamedTypeSymbol type) =>
      type is { TypeKind: TypeKind.Class, IsAbstract: false, DeclaredAccessibility: Accessibility.Public, ContainingType: null, TypeParameters.Length: 0 }
      && type.ContainingModule.ReferencedAssemblySymbols.Any(assembly => assembly.Name == MvcAssemblyName)
      && !SelfAndBaseTypes(type).Any(current => HasAttribute(current, NonControllerAttributeName))
      && (type.Name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)
         || SelfAndBaseTypes(type).Any(current => HasAttribute(current, ControllerAttributeName)));

   /// <summary>
   /// Returns whether Razor Pages runs the method as a handler of a page model.
   /// </summary>
   /// <remarks>
   /// It follows <see href="https://github.com/dotnet/aspnetcore/blob/3a3c6d0b75c1f26c3a4c15cd2b786fb9929c4136/src/Mvc/Mvc.RazorPages/src/ApplicationModels/DefaultPageApplicationModelPartsProvider.cs#L148-L262">IsHandler and TryParseHandlerMethod</see>:
   /// the name is <c>On</c>, then at least one more character before an optional <c>Async</c> suffix.
   /// </remarks>
   private static bool IsPageHandler(IMethodSymbol method)
   {
      if (method is not { MethodKind: MethodKind.Ordinary, DeclaredAccessibility: Accessibility.Public, IsStatic: false, IsAbstract: false, IsGenericMethod: false }
         || method.ContainingType is not { } type
         || !DerivesFrom(type, PageModelName)
         || SelfAndOverridden(method).Any(overridden => HasAttribute(overridden, NonHandlerAttributeName)))
      {
         return false;
      }

      var declaringType = BaseDefinition(method).ContainingType;
      if (declaringType.SpecialType == SpecialType.System_Object || declaringType.ToDisplayString() == PageModelName)
         return false;

      var name = method.Name;
      var length = name.EndsWith("Async", StringComparison.Ordinal) ? name.Length - "Async".Length : name.Length;
      return name.StartsWith("On", StringComparison.Ordinal) && length > "On".Length;
   }

   /// <summary>
   /// Returns whether SignalR lets clients call the method of a hub.
   /// </summary>
   /// <remarks>
   /// It follows <see href="https://github.com/dotnet/aspnetcore/blob/3a3c6d0b75c1f26c3a4c15cd2b786fb9929c4136/src/SignalR/server/Core/src/Internal/HubReflectionHelper.cs">HubReflectionHelper</see>.
   /// </remarks>
   private static bool IsHubMethod(IMethodSymbol method)
   {
      if (method is not { MethodKind: MethodKind.Ordinary, DeclaredAccessibility: Accessibility.Public, IsStatic: false }
         || method.ContainingType is not { } type
         || !DerivesFrom(type, HubName)
         || IsDisposeMethod(method))
      {
         return false;
      }

      var declaringType = BaseDefinition(method).ContainingType;
      return declaringType.SpecialType != SpecialType.System_Object && declaringType.OriginalDefinition.ToDisplayString() is not (HubName or $"{HubName}<T>");
   }

   private static bool IsDisposeMethod(IMethodSymbol method) =>
      method is { Name: "Dispose", Parameters.Length: 0 }
      && method.ContainingType.AllInterfaces.Any(@interface => @interface.SpecialType == SpecialType.System_IDisposable);

   private static bool DerivesFrom(INamedTypeSymbol type, string baseTypeName) =>
      SelfAndBaseTypes(type.BaseType).Any(current => current.OriginalDefinition.ToDisplayString() is var name
         && (name == baseTypeName || name.StartsWith($"{baseTypeName}<", StringComparison.Ordinal)));

   private static bool HasAttribute(ISymbol symbol, string attributeName) =>
      symbol.GetAttributes().Any(attribute => SelfAndBaseTypes(attribute.AttributeClass).Any(current => current.ToDisplayString() == attributeName));

   private static IEnumerable<INamedTypeSymbol> SelfAndBaseTypes(INamedTypeSymbol? type)
   {
      for (var current = type; current is not null; current = current.BaseType)
         yield return current;
   }

   private static IEnumerable<IMethodSymbol> SelfAndOverridden(IMethodSymbol method)
   {
      for (var current = method; current is not null; current = current.OverriddenMethod)
         yield return current;
   }

   private static IMethodSymbol BaseDefinition(IMethodSymbol method) => SelfAndOverridden(method).Last();
}
