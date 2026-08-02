namespace ECert.Models.ApiModels;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data, string message = "تمت العملية بنجاح")
        => new() { Success = true, Message = message, Data = data };

    public static ApiResponse<T> Fail(string message)
        => new() { Success = false, Message = message };
}

public class ApiResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    public static ApiResponse Ok(string message = "تمت العملية بنجاح")
        => new() { Success = true, Message = message };

    public static ApiResponse Fail(string message)
        => new() { Success = false, Message = message };
}
