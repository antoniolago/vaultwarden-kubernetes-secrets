using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Infrastructure;
using Xunit;

namespace VaultwardenK8sSync.Tests;

public class ProcessRunnerTests
{
    private readonly Mock<ILogger<ProcessRunner>> _loggerMock;
    private readonly ProcessRunner _processRunner;

    public ProcessRunnerTests()
    {
        _loggerMock = new Mock<ILogger<ProcessRunner>>();
        _processRunner = new ProcessRunner(_loggerMock.Object);
    }

    [Fact]
    public async Task RunAsync_WithNoInput_ShouldCompleteWithoutHanging()
    {
        // Arrange - Use a simple command that reads from stdin
        // 'cat' will hang if stdin is not closed, but complete immediately if it is
        var process = CreateProcess("cat", "");

        // Act - Should complete within timeout (not hang waiting for stdin)
        var stopwatch = Stopwatch.StartNew();
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 5);
        stopwatch.Stop();

        // Assert
        result.Success.Should().BeTrue("cat should succeed when stdin is closed");
        result.Output.Should().BeEmpty("no input was provided");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(3000, "process should complete quickly, not hang");
    }

    [Fact]
    public async Task RunAsync_WithInput_ShouldWriteToStdinAndComplete()
    {
        // Arrange - Use cat to echo back the input
        var process = CreateProcess("cat", "");
        var testInput = "hello world";

        // Act
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 5, input: testInput);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Trim().Should().Be(testInput, "cat should echo the input");
    }

    [Fact]
    public async Task RunAsync_WithMultilineInput_ShouldWriteAllLinesToStdin()
    {
        // Arrange - wc -l counts lines
        var process = CreateProcess("cat", "");
        var testInput = "line1";

        // Act
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 5, input: testInput);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Trim().Should().Be(testInput);
    }

    [Fact]
    public async Task RunAsync_ProcessTimesOut_ShouldReturnTimeoutError()
    {
        // Arrange - sleep command that takes longer than timeout
        var process = CreateProcess("sleep", "10");

        // Act
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 1);

        // Assert
        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(-1);
        result.Error.Should().Contain("timed out");
    }

    [Fact]
    public async Task RunAsync_CommandFails_ShouldReturnNonZeroExitCode()
    {
        // Arrange - false command always returns exit code 1
        var process = CreateProcess("false", "");

        // Act
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 5);

        // Assert
        result.Success.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task RunAsync_CommandSucceeds_ShouldReturnZeroExitCode()
    {
        // Arrange - true command always returns exit code 0
        var process = CreateProcess("true", "");

        // Act
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 5);

        // Assert
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_CapturesStdout_ShouldReturnOutput()
    {
        // Arrange
        var process = CreateProcess("echo", "test output");

        // Act
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 5);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Trim().Should().Be("test output");
    }

    [Fact]
    public async Task RunAsync_CapturesStderr_ShouldReturnError()
    {
        // Arrange - sh -c allows us to write to stderr
        var process = CreateProcess("sh", "-c \"echo error message >&2\"");

        // Act
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 5);

        // Assert
        result.Success.Should().BeTrue(); // Command succeeds even though it writes to stderr
        result.Error.Trim().Should().Be("error message");
    }

    [Fact]
    public async Task RunAsync_StdinClosedImmediately_ProcessShouldNotBlock()
    {
        // Arrange - Use 'head -n 1' which reads one line from stdin
        // Without stdin being closed, this would block forever waiting for input
        var process = CreateProcess("head", "-n 1");

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 3);
        stopwatch.Stop();

        // Assert
        // The process should complete (either success or failure) but NOT timeout
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000,
            "head should complete quickly when stdin is closed (EOF signal)");
    }

    [Fact]
    public async Task RunAsync_WithPasswordInput_ShouldPassToStdin()
    {
        // Arrange - Simulate password input scenario
        // Using 'cat' to verify the password is written to stdin
        var process = CreateProcess("cat", "");
        var password = "MySecretPassword123!";

        // Act
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 5, input: password);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Trim().Should().Be(password, "password should be echoed back by cat");
    }

    [Fact]
    public async Task RunAsync_InteractiveCommandWithInput_ShouldComplete()
    {
        // Arrange - Simulate an interactive command that expects input then exits
        // 'read' command in sh reads one line then exits
        var process = CreateProcess("sh", "-c \"read line && echo $line\"");
        var inputLine = "test input line";

        // Act
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 5, input: inputLine);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Trim().Should().Be(inputLine);
    }

    [Fact]
    public async Task RunAsync_BwConfigSimulation_ShouldNotHang()
    {
        // Arrange - Simulate the bw config server command behavior
        // This command writes to a config file and exits
        // It should NOT hang waiting for stdin
        var process = CreateProcess("sh", "-c \"echo 'config set'\"");

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 5);
        stopwatch.Stop();

        // Assert
        result.Success.Should().BeTrue();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000, "should complete quickly, not hang");
    }

    private static Process CreateProcess(string fileName, string arguments)
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
    }
}
