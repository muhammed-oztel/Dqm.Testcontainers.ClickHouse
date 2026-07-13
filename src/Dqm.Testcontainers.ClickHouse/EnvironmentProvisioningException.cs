namespace Dqm.Testcontainers.ClickHouse;

public sealed class EnvironmentProvisioningException : Exception
{
    public EnvironmentProvisioningException(string message, Exception inner) : base(message, inner)
    {
    }
}
