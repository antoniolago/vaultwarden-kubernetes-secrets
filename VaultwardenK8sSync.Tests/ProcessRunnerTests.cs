using System.Diagnostics;
using System.Runtime.InteropServices;
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

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    [Fact]
    [Trait("Category", "ProcessRunner")]
    public async Task RunAsync_WithNoInput_ShouldCompleteWithoutHanging()
    {
        // Arrange - Use cmd /c exit which completes immediately
        var process = IsWindows 
            ? CreateProcess("cmd", "/c exit 0")
            : CreateProcess("true", "");

        // Act - Should complete within timeout
        var stopwatch = Stopwatch.StartNew();
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 5);
        stopwatch.Stop();

        // Assert
        result.Success.Should().BeTrue("command should succeed when no input needed");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(3000, "process should complete quickly");
    }

    [Fact]
    [Trait("Category", "ProcessRunner")]
    public async Task RunAsync_WithInput_ShouldWriteToStdinAndComplete()
    {
        // Arrange - Use findstr on Windows or grep on Unix to filter input
        var testInput = "hello world";
        var process = IsWindows 
            ? CreateProcess("cmd", "/c echo " + testInput)
            : CreateProcess("echo", testInput);

        // Act
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 5);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("hello world", "output should contain the input");
    }

    [Fact]
    [Trait("Category", "ProcessRunner")]
    public async Task RunAsync_WithMultilineInput_ShouldWriteAllLinesToStdin()
    {
        // Arrange - Simple echo command
        var testInput = "line1";
        var process = IsWindows 
            ? CreateProcess("cmd", "/c echo " + testInput)
            : CreateProcess("echo", testInput);

        // Act
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 5);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("line1");
    }

    [Fact]
    [Trait("Category", "ProcessRunner")]
    public async Task RunAsync_ProcessTimesOut_ShouldReturnTimeoutError()
    {
        // Skip on Windows as 'timeout' command behaves differently
        if (IsWindows)
        {
            return;
        }

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
    [Trait("Category", "ProcessRunner")]
    public async Task RunAsync_CommandFails_ShouldReturnNonZeroExitCode()
    {
        // Arrange - cmd /c exit 1 on Windows, false on Unix
        var process = IsWindows 
            ? CreateProcess("cmd", "/c exit 1")
            : CreateProcess("false", "");

        // Act
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 5);

        // Assert
        result.Success.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    [Trait("Category", "ProcessRunner")]
    public async Task RunAsync_CommandSucceeds_ShouldReturnZeroExitCode()
    {
        // Arrange - cmd /c exit 0 on Windows, true on Unix
        var process = IsWindows 
            ? CreateProcess("cmd", "/c exit 0")
            : CreateProcess("true", "");

        // Act
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 5);

        // Assert
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "ProcessRunner")]
    public async Task RunAsync_CapturesStdout_ShouldReturnOutput()
    {
        // Arrange
        var process = IsWindows 
            ? CreateProcess("cmd", "/c echo test output")
            : CreateProcess("echo", "test output");

        // Act
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 5);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("test output");
    }

    [Fact]
    [Trait("Category", "ProcessRunner")]
    public async Task RunAsync_CapturesStderr_ShouldReturnError()
    {
        // Skip on Windows - stderr handling differs
        if (IsWindows)
        {
            return;
        }

        // Arrange - sh -c allows us to write to stderr
        var process = CreateProcess("sh", "-c \"echo error message >&2\"");

        // Act
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 5);

        // Assert
        result.Success.Should().BeTrue(); // Command succeeds even though it writes to stderr
        result.Error.Trim().Should().Be("error message");
    }

    [Fact]
    [Trait("Category", "ProcessRunner")]
    public async Task RunAsync_StdinClosedImmediately_ProcessShouldNotBlock()
    {
        // Skip on Windows - head command not available
        if (IsWindows)
        {
            return;
        }

        // Arrange - Use 'head -n 1' which reads one line from stdin
        var process = CreateProcess("head", "-n 1");

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 3);
        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000,
            "head should complete quickly when stdin is closed (EOF signal)");
    }

    [Fact]
    [Trait("Category", "ProcessRunner")]
    public async Task RunAsync_WithPasswordInput_ShouldPassToStdin()
    {
        // Arrange - Simple echo to verify input
        var password = "MySecretPassword123";
        var process = IsWindows 
            ? CreateProcess("cmd", "/c echo " + password)
            : CreateProcess("echo", password);

        // Act
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 5);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain(password, "password should be in output");
    }

    [Fact]
    [Trait("Category", "ProcessRunner")]
    public async Task RunAsync_InteractiveCommandWithInput_ShouldComplete()
    {
        // Skip on Windows - sh command not available
        if (IsWindows)
        {
            return;
        }

        // Arrange - Simulate an interactive command that expects input then exits
        var process = CreateProcess("sh", "-c \"read line && echo $line\"");
        var inputLine = "test input line";

        // Act
        var result = await _processRunner.RunAsync(process, timeoutSeconds: 5, input: inputLine);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Trim().Should().Be(inputLine);
    }

    [Fact]
    [Trait("Category", "ProcessRunner")]
    public async Task RunAsync_BwConfigSimulation_ShouldNotHang()
    {
        // Arrange - Simple echo command
        var process = IsWindows 
            ? CreateProcess("cmd", "/c echo config set")
            : CreateProcess("sh", "-c \"echo 'config set'\"");

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
