using System.Reflection;
using API.Controllers;
using BLL.Services;
using Microsoft.EntityFrameworkCore;

namespace BLL.Tests
{
    public class ArchitectureTests
    {
        private static readonly Assembly ApiAssembly = typeof(AuthController).Assembly;
        private static readonly Assembly BllAssembly = typeof(UserService).Assembly;

        [Fact]
        public void Controllers_Should_Not_Depend_On_Repositories_DbContext_Or_Service_Implementations()
        {
            var violations = GetConcreteTypes(ApiAssembly, "API.Controllers")
                .SelectMany(controllerType => controllerType.GetConstructors()
                    .SelectMany(ctor => ctor.GetParameters()
                        .Where(parameter =>
                            IsRepositoryDependency(parameter.ParameterType) ||
                            IsDbContextDependency(parameter.ParameterType) ||
                            IsConcreteServiceImplementation(parameter.ParameterType))
                        .Select(parameter => $"{controllerType.FullName} -> {FormatType(parameter.ParameterType)}")))
                .ToList();

            Assert.True(
                violations.Count == 0,
                $"Controllers must depend on BLL abstractions or framework services only. Violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
        }

        [Fact]
        public void Services_Should_Not_Depend_On_Api_DbContext_Or_Concrete_Repositories()
        {
            var violations = GetConcreteTypes(BllAssembly, "BLL.Services")
                .Where(serviceType => serviceType.Name.EndsWith("Service", StringComparison.Ordinal))
                .SelectMany(serviceType => serviceType.GetConstructors()
                    .SelectMany(ctor => ctor.GetParameters()
                        .Where(parameter =>
                            IsApiDependency(parameter.ParameterType) ||
                            IsDbContextDependency(parameter.ParameterType) ||
                            IsConcreteRepositoryDependency(parameter.ParameterType) ||
                            IsConcreteServiceImplementation(parameter.ParameterType))
                        .Select(parameter => $"{serviceType.FullName} -> {FormatType(parameter.ParameterType)}")))
                .ToList();

            Assert.True(
                violations.Count == 0,
                $"BLL services must depend on abstractions, not API/concrete repositories/DbContext. Violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
        }

        private static IEnumerable<Type> GetConcreteTypes(Assembly assembly, string namespacePrefix)
        {
            return assembly.GetTypes()
                .Where(type =>
                    type.IsClass &&
                    !type.IsAbstract &&
                    type.Namespace?.StartsWith(namespacePrefix, StringComparison.Ordinal) == true);
        }

        private static bool IsApiDependency(Type dependencyType)
        {
            var coreType = GetCoreType(dependencyType);
            return coreType.Namespace?.StartsWith("API", StringComparison.Ordinal) == true;
        }

        private static bool IsRepositoryDependency(Type dependencyType)
        {
            var coreType = GetCoreType(dependencyType);
            return coreType.Name.EndsWith("Repository", StringComparison.Ordinal) ||
                   coreType.Namespace?.StartsWith("DAL", StringComparison.Ordinal) == true;
        }

        private static bool IsConcreteRepositoryDependency(Type dependencyType)
        {
            var coreType = GetCoreType(dependencyType);
            return !coreType.IsInterface &&
                   (coreType.Name.EndsWith("Repository", StringComparison.Ordinal) ||
                    coreType.Namespace?.StartsWith("DAL.Repositories", StringComparison.Ordinal) == true);
        }

        private static bool IsConcreteServiceImplementation(Type dependencyType)
        {
            var coreType = GetCoreType(dependencyType);
            return coreType.Namespace == "BLL.Services" && !coreType.IsInterface;
        }

        private static bool IsDbContextDependency(Type dependencyType)
        {
            var coreType = GetCoreType(dependencyType);
            return typeof(DbContext).IsAssignableFrom(coreType) ||
                   coreType.Name == "ApplicationDbContext";
        }

        private static Type GetCoreType(Type dependencyType)
        {
            return dependencyType.IsGenericType ? dependencyType.GetGenericTypeDefinition() : dependencyType;
        }

        private static string FormatType(Type dependencyType)
        {
            var coreType = GetCoreType(dependencyType);
            return coreType.FullName ?? coreType.Name;
        }
    }
}
