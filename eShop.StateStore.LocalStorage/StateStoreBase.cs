using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;

namespace eShop.StateStore.LocalStorage
{
    public class StateStoreBase
    {
        protected Action listeners;

        public void AddStateChangeListeners(Action listener)
            => this.listeners += listener;


        public void RemoveStateChangeListeners(Action listener)
            => this.listeners -= listener;


        public void BroadCastStateChange()
        {
            // Notifications follow a completed cart operation. A stale or faulty view
            // must not make that successful operation appear to have failed, or prevent
            // the other subscribed views from refreshing.
            var subscribers = this.listeners?.GetInvocationList();
            if (subscribers == null) return;
            foreach (Action subscriber in subscribers)
            {
                try { subscriber(); }
                catch (Exception exception)
                {
                    Trace.TraceError("MyShop cart state listener failed: {0}", exception);
                }
            }
        }

    }
}
