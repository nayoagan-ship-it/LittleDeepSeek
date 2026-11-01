using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace LittleDeepSeek
{
    // A skin owns the companion's name. Share this identity with open utility
    // windows so changing skins updates them without recreating user state.
    internal sealed class PetIdentity
    {
        private sealed class Binding
        {
            internal Control Control;
            internal string Text, AccessibleName;
        }
        private readonly List<Binding> bindings = new List<Binding>();
        internal string Name { get; private set; }
        internal event EventHandler Changed;
        internal PetIdentity() { Name = "小deepseek"; }

        internal void Bind(Control control, string text, string accessibleName = null)
        {
            Binding binding = new Binding { Control = control, Text = text, AccessibleName = accessibleName };
            bindings.Add(binding);
            control.Disposed += delegate { bindings.Remove(binding); };
            Update(binding);
        }

        private void Update(Binding binding)
        {
            if (binding.Control.IsDisposed) return;
            binding.Control.Text = binding.Text.Replace("{pet}", Name);
            if (binding.AccessibleName != null) binding.Control.AccessibleName = binding.AccessibleName.Replace("{pet}", Name);
        }

        internal void SetName(string name)
        {
            if (Name == name) return;
            Name = name;
            foreach (Binding binding in bindings.ToArray()) Update(binding);
            if (Changed != null) Changed(this, EventArgs.Empty);
        }
    }

}
