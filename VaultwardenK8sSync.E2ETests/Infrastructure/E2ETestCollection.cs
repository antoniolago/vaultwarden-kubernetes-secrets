using Xunit;

namespace VaultwardenK8sSync.E2ETests.Infrastructure;

/// <summary>
/// Collection definition for E2E tests that share the same test fixture.
/// All tests in this collection share the same Kind cluster and Vaultwarden instance.
/// </summary>
[CollectionDefinition("E2E")]
public class E2ETestCollection : ICollectionFixture<E2ETestFixture>
{
}
