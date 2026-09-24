using System.Reflection;
using System.Runtime.CompilerServices;
using Vaulta.Identity.Application;
using Vaulta.Identity.Contracts;
using Vaulta.Identity.Domain;
using Vaulta.Identity.Infrastructure;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.ArchitectureTests;

public sealed class DependencyTests
{
    [Fact] public void DomainReferencesOnlySharedKernelAndPlatform() => AssertReferences(typeof(User).Assembly, "Vaulta.SharedKernel");
    [Fact] public void ApplicationDoesNotReferenceApiOrInfrastructure() =>
        AssertReferences(typeof(CommandHandlers).Assembly, "Vaulta.SharedKernel", "Vaulta.Identity.Domain", "Vaulta.Identity.Contracts", "FluentValidation");
    [Fact] public void ContractsAreIndependent() => AssertReferences(typeof(MyProfileDto).Assembly);
    [Fact] public void SharedKernelIsIndependent() => AssertReferences(typeof(Money).Assembly);
    [Fact] public void ModulesDoNotExposeInternalsToOtherAssemblies()
    {
        foreach (var assembly in new[] { typeof(User).Assembly, typeof(CommandHandlers).Assembly, typeof(IdentityDbContext).Assembly })
            Assert.Empty(assembly.GetCustomAttributes<InternalsVisibleToAttribute>());
    }
    [Fact] public void ApplicationDoesNotExposeQueryable()
    {
        foreach (var method in typeof(IIdentityStore).Assembly.GetExportedTypes().SelectMany(t => t.GetMethods()))
            Assert.False(method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(IQueryable<>));
    }
    private static void AssertReferences(Assembly assembly, params string[] allowed)
    {
        foreach (var reference in assembly.GetReferencedAssemblies())
            Assert.True(reference.Name!.StartsWith("System") || reference.Name == "netstandard" || allowed.Contains(reference.Name), $"Forbidden reference: {reference.Name}");
    }
}
