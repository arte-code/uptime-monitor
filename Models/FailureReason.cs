namespace UptimeMonitor.Models;

public enum FailureReason
{
    Timeout,
    DnsFailure,
    ConnectionFailed,
    TlsFailure,
    InvalidResponse,
    Unknown,
}
