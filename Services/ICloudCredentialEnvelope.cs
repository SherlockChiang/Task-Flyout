namespace Task_Flyout.Services
{
    internal sealed record ICloudCredentialEnvelope(string AccountName, string AppSpecificPassword)
    {
        public override string ToString()
            => $"{nameof(ICloudCredentialEnvelope)} {{ Credentials = [REDACTED] }}";
    }
}
