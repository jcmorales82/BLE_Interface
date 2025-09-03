using System.Collections.ObjectModel;

namespace BLE_Interface.Helpers
{
    /// <summary>
    /// ObservableCollection with a maximum capacity.
    /// When capacity is exceeded, it removes items from the start.
    /// </summary>
    public sealed class BoundedObservableCollection<T> : ObservableCollection<T>
    {
        public int Capacity { get; set; }

        public BoundedObservableCollection(int capacity) => Capacity = capacity;

        protected override void InsertItem(int index, T item)
        {
            // Always append at the end to keep UI virtualization happy
            base.InsertItem(Count, item);

            if (Capacity > 0 && Count > Capacity)
            {
                // Drop oldest item(s)
                RemoveAt(0);
            }
        }

        /// <summary>
        /// Adds a batch efficiently.
        /// </summary>
        public void AddRange(System.Collections.Generic.IEnumerable<T> items)
        {
            foreach (var i in items) InsertItem(Count, i);
        }
    }
}
