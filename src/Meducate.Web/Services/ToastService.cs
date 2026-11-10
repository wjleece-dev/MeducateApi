namespace Meducate.Web.Services;

public enum ToastType
{
    Success,
    Error,
    Info
}

public class Toast
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Message { get; }
    public ToastType Type { get; }
    public bool IsDismissing { get; set; }
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    public Toast(string message, ToastType type)
    {
        Message = message;
        Type = type;
    }
}

public class ToastService
{
    private readonly List<Toast> _toasts = [];
    private readonly object _lock = new();

    public IReadOnlyList<Toast> Toasts
    {
        get { lock (_lock) { return _toasts.ToList(); } }
    }

    public event Action? OnChange;

    public void Show(string message, ToastType type = ToastType.Info)
    {
        var toast = new Toast(message, type);
        lock (_lock) { _toasts.Add(toast); }
        OnChange?.Invoke();

        _ = DismissAfterDelay(toast.Id, type == ToastType.Error ? 5000 : 3000);
    }

    public void Success(string message) => Show(message, ToastType.Success);
    public void Error(string message) => Show(message, ToastType.Error);

    public void Dismiss(Guid id)
    {
        bool shouldAnimate;
        lock (_lock)
        {
            var toast = _toasts.FirstOrDefault(t => t.Id == id);
            if (toast is not null && !toast.IsDismissing)
            {
                toast.IsDismissing = true;
                shouldAnimate = true;
            }
            else
            {
                shouldAnimate = false;
            }
        }

        if (shouldAnimate)
        {
            OnChange?.Invoke();
            _ = RemoveAfterAnimation(id);
        }
    }

    private async Task DismissAfterDelay(Guid id, int delayMs)
    {
        try
        {
            await Task.Delay(delayMs);
            Dismiss(id);
        }
        catch (OperationCanceledException) { }
    }

    private async Task RemoveAfterAnimation(Guid id)
    {
        try
        {
            await Task.Delay(300); // Match CSS animation duration
            bool removed;
            lock (_lock)
            {
                var toast = _toasts.FirstOrDefault(t => t.Id == id);
                removed = toast is not null && _toasts.Remove(toast);
            }

            if (removed)
                OnChange?.Invoke();
        }
        catch (OperationCanceledException) { }
    }
}
