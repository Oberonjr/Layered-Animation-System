using UnityEngine.Networking;
using System.Text;

/// <summary>
/// Custom download handler for streaming Ollama responses
/// </summary>
public class StreamingDownloadHandler : DownloadHandlerScript
{
    private StringBuilder receivedData = new StringBuilder();
    private int lastProcessedLength = 0;

    public StreamingDownloadHandler() : base(new byte[1024 * 1024]) // 1MB buffer
    {
    }

    protected override bool ReceiveData(byte[] data, int dataLength)
    {
        if (data == null || dataLength == 0)
            return false;

        string text = Encoding.UTF8.GetString(data, 0, dataLength);
        receivedData.Append(text);
        
        return true;
    }

    public bool HasNewData()
    {
        return receivedData.Length > lastProcessedLength;
    }

    public string GetNewText()
    {
        if (!HasNewData())
            return string.Empty;

        string newText = receivedData.ToString(lastProcessedLength, receivedData.Length - lastProcessedLength);
        lastProcessedLength = receivedData.Length;
        return newText;
    }

    protected override void CompleteContent()
    {
        base.CompleteContent();
    }
}
