using UnityEngine;

public class TurnTransportProvider : MonoBehaviour
{
    public enum TransportKind
    {
        InMemory,
        Null
    }

    public TransportKind kind = TransportKind.InMemory;

    private ITurnTransport transport;
    private TransportKind initializedKind;

    public ITurnTransport GetTransport()
    {
        if (transport == null || initializedKind != kind)
        {
            initializedKind = kind;
            transport = kind == TransportKind.Null ? new NullTurnTransport() : new InMemoryTurnTransport();
            transport.Initialize();
        }

        return transport;
    }
}

