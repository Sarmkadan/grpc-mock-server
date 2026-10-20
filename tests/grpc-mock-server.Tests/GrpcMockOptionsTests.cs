namespace GrpcMockServer.Tests;

public class GrpcMockOptionsTests
{
    [Fact]
    public void Validate_Defaults_DoesNotThrow()
    {
        var options = new GrpcMockOptions();
        options.Validate();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(-100)]
    [InlineData(70000)]
    public void Validate_InvalidPort_ThrowsArgumentException(int port)
    {
        var options = new GrpcMockOptions { Port = port };

        var ex = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("Port", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(80)]
    [InlineData(443)]
    [InlineData(65535)]
    public void Validate_ValidPort_DoesNotThrow(int port)
    {
        var options = new GrpcMockOptions { Port = port };
        options.Validate();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Validate_NullOrWhitespaceHost_ThrowsArgumentException(string? host)
    {
        var options = new GrpcMockOptions { Host = host! };

        var ex = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("Host", ex.Message);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("example.com")]
    [InlineData("abc.def.ghi.jkl")]
    public void Validate_InvalidHost_ThrowsArgumentException(string host)
    {
        var options = new GrpcMockOptions { Host = host };

        var ex = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("Host", ex.Message);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("192.168.1.1")]
    public void Validate_ValidHost_DoesNotThrow(string host)
    {
        var options = new GrpcMockOptions { Host = host };
        options.Validate();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_InvalidMaxRecordedCalls_ThrowsArgumentException(int maxCalls)
    {
        var options = new GrpcMockOptions { MaxRecordedCalls = maxCalls };

        var ex = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("MaxRecordedCalls", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1024)]
    public void Validate_InvalidMaxReceiveMessageSize_ThrowsArgumentException(int size)
    {
        var options = new GrpcMockOptions { MaxReceiveMessageSize = size };

        var ex = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("MaxReceiveMessageSize", ex.Message);
    }

    [Fact]
    public void Validate_NullMaxReceiveMessageSize_DoesNotThrow()
    {
        var options = new GrpcMockOptions { MaxReceiveMessageSize = null };
        options.Validate();
    }

    [Fact]
    public void Validate_EnableReflection_ThrowsNotSupportedException()
    {
#pragma warning disable CS0618
        var options = new GrpcMockOptions { EnableReflection = true };
#pragma warning restore CS0618

        var ex = Assert.Throws<NotSupportedException>(() => options.Validate());
        Assert.Contains("Reflection is not supported", ex.Message);
    }

    [Fact]
    public void Clone_ReturnsIndependentCopy()
    {
        var original = new GrpcMockOptions
        {
            UseTestHost = false,
            Port = 5000,
            Host = "localhost",
            RecordRequests = false,
            MaxRecordedCalls = 500,
            MaxReceiveMessageSize = 1024,
        };

        var clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.Equal(original.UseTestHost, clone.UseTestHost);
        Assert.Equal(original.Port, clone.Port);
        Assert.Equal(original.Host, clone.Host);
        Assert.Equal(original.RecordRequests, clone.RecordRequests);
        Assert.Equal(original.MaxRecordedCalls, clone.MaxRecordedCalls);
        Assert.Equal(original.MaxReceiveMessageSize, clone.MaxReceiveMessageSize);

        // Mutating original does not affect clone
        original.Port = 9999;
        original.MaxRecordedCalls = 1;
        Assert.Equal(5000, clone.Port);
        Assert.Equal(500, clone.MaxRecordedCalls);
    }

    [Fact]
    public void RealSocket_DefaultParameters_ReturnsNonTestHostOptions()
    {
        var options = GrpcMockOptions.RealSocket();

        Assert.False(options.UseTestHost);
        Assert.Equal(0, options.Port);
        Assert.Equal("127.0.0.1", options.Host);
    }

    [Fact]
    public void RealSocket_CustomParameters_SetsPortAndHost()
    {
        var options = GrpcMockOptions.RealSocket(port: 8080, host: "localhost");

        Assert.False(options.UseTestHost);
        Assert.Equal(8080, options.Port);
        Assert.Equal("localhost", options.Host);
    }
}
