namespace VaultwardenK8sSync.Models;

public class OperationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public static OperationResult Successful() => new() { Success = true };
    public static OperationResult Failed(string errorMessage) => new() { Success = false, ErrorMessage = errorMessage };
}

public class OperationResult<T> : OperationResult
{
    public T? Data { get; set; }

    public static OperationResult<T> Successful(T data) => new() { Success = true, Data = data };
    public static new OperationResult<T> Failed(string errorMessage) => new() { Success = false, ErrorMessage = errorMessage };
}
