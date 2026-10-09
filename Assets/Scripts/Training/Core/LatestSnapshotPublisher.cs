using System;
using System.Collections.Generic;
using System.Threading;

namespace BlockNations.Training
{
    // Optional spectator output: producers replace pending values, never wait for
    // a file write or a consumer. At most capacity pending values plus one in flight.
    public sealed class LatestSnapshotPublisher<T> : IDisposable
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, T> pending = new Dictionary<string, T>();
        private readonly Queue<string> keys = new Queue<string>();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Action<string, T> write;
        private readonly Thread thread;
        private bool closing;
        private long superseded;
        private volatile string error;
        public int Capacity { get; }
        public int PendingCount { get { lock (gate) return pending.Count; } }
        public long Superseded { get { lock (gate) return superseded; } }
        public string Error => error;

        public LatestSnapshotPublisher(int capacity, Action<string, T> write)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity; this.write = write ?? throw new ArgumentNullException(nameof(write));
            thread = new Thread(Drain) { IsBackground = true, Name = "Training spectator publisher" };
            thread.Start();
        }

        public void Publish(string key, T value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            lock (gate)
            {
                if (closing) return;
                if (pending.ContainsKey(key)) superseded++;
                else
                {
                    if (pending.Count == Capacity) { pending.Remove(keys.Dequeue()); superseded++; }
                    keys.Enqueue(key);
                }
                pending[key] = value;
                wake.Set();
            }
        }

        private void Drain()
        {
            try
            {
                while (true)
                {
                    string key = null; T value = default;
                    lock (gate)
                    {
                        if (keys.Count > 0) { key = keys.Dequeue(); value = pending[key]; pending.Remove(key); }
                        else if (closing) return;
                    }
                    if (key == null) { wake.WaitOne(); continue; }
                    try { write(key, value); error = null; }
                    catch (Exception failure) { error = "Spectator output: " + failure.Message; }
                }
            }
            finally { wake.Dispose(); }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (!closing) { closing = true; wake.Set(); }
            }
            // Shutdown may flush optional records; no simulation step joins this thread.
            thread.Join(2000);
        }
    }
}
