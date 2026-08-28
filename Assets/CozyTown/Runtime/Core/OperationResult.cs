namespace CozyTown.Runtime.Core
{
    public readonly struct OperationResult
    {
        private OperationResult(bool isSuccess, string errorCode)
        {
            IsSuccess = isSuccess;
            ErrorCode = errorCode ?? string.Empty;
        }

        public bool IsSuccess { get; }

        public string ErrorCode { get; }

        public static OperationResult Success()
        {
            return new OperationResult(true, string.Empty);
        }

        public static OperationResult Failure(string errorCode)
        {
            return new OperationResult(false, errorCode);
        }
    }

    public readonly struct OperationResult<T>
    {
        private OperationResult(bool isSuccess, T value, string errorCode)
        {
            IsSuccess = isSuccess;
            Value = value;
            ErrorCode = errorCode ?? string.Empty;
        }

        public bool IsSuccess { get; }

        public T Value { get; }

        public string ErrorCode { get; }

        public static OperationResult<T> Success(T value)
        {
            return new OperationResult<T>(true, value, string.Empty);
        }

        public static OperationResult<T> Failure(string errorCode)
        {
            return new OperationResult<T>(false, default, errorCode);
        }
    }
}
