using Microsoft.Diagnostics.Runtime;
using Microsoft.VisualBasic;
using System.Dynamic;
using System.Runtime.InteropServices;
using System.Text;


int pid = int.Parse(args[0]);
StringBuilder log = new StringBuilder();

// Create the data target.  This tells us the versions of CLR loaded in the target process.
using (DataTarget dataTarget = DataTarget.AttachToProcess(pid, true))
{
    // Now check bitness of our program/target:
    bool isTarget64Bit = dataTarget.DataReader.PointerSize == 8;
    if (Environment.Is64BitProcess != isTarget64Bit)
        throw new Exception(string.Format("Architecture mismatch:  Process is {0} but target is {1}", Environment.Is64BitProcess ? "64 bit" : "32 bit", isTarget64Bit ? "64 bit" : "32 bit"));

    // Note I just take the first version of CLR in the process.  You can loop over every loaded
    // CLR to handle the SxS case where both desktop CLR and .Net Core are loaded in the process.
    ClrInfo version = dataTarget.ClrVersions[0];

    // Now that we have the DataTarget, the version of CLR, and the right dac, we create and return a
    // ClrRuntime instance.
    using ClrRuntime runtime = version.CreateRuntime();

    // Walk each thread in the process.
    foreach (ClrThread thread in runtime.Threads)
    {
        // The ClrRuntime.Threads will also report threads which have recently died, but their
        // underlying datastructures have not yet been cleaned up.  This can potentially be
        // useful in debugging (!threads displays this information with XXX displayed for their
        // OS thread id).  You cannot walk the stack of these threads though, so we skip them
        // here.
        if (!thread.IsAlive)
            continue;

        log.AppendLine($"Thread OSThreadId: {thread.OSThreadId}");

        // Each thread tracks a "last thrown exception".  This is the exception object which
        // !threads prints.  If that exception object is present, we will display some basic
        // exception data here.  Note that you can get the stack trace of the exception with
        // ClrHeapException.StackTrace (we don't do that here).
        ClrException? currException = thread.CurrentException;
        if (currException is ClrException ex)
            log.AppendLine($"Exception: {ex.Address} ({ex.Type.Name}), HRESULT={ex.HResult}");

        // Walk the stack of the thread and print output similar to !ClrStack.
        log.AppendLine();
        log.AppendLine("Managed Callstack:");
        foreach (ClrStackFrame frame in thread.EnumerateStackTrace())
        {
            // Note that CLRStackFrame currently only has three pieces of data: stack pointer,
            // instruction pointer, and frame name (which comes from ToString).  Future
            // versions of this API will allow you to get the type/function/module of the
            // method (instead of just the name).  This is not yet implemented.
            log.AppendLine($"{frame}");
        }

        // Print a !DumpStackObjects equivalent.

        //{
        //    // We'll need heap data to find objects on the stack.
        //    ClrHeap heap = runtime.Heap;

        //    // Walk each pointer aligned address on the stack.  Note that StackBase/StackLimit
        //    // is exactly what they are in the TEB.  This means StackBase > StackLimit on AMD64.
        //    ulong start = thread.StackBase;
        //    ulong stop = thread.StackLimit;

        //    // We'll walk these in pointer order.
        //    if (start > stop)
        //    {
        //        ulong tmp = start;
        //        start = stop;
        //        stop = tmp;
        //    }

        //    log.AppendLine();
        //    log.AppendLine("Stack objects:");

        //    // Walk each pointer aligned address.  Ptr is a stack address.
        //    for (ulong ptr = start; ptr <= stop; ptr += (uint)IntPtr.Size)
        //    {
        //        // Read the value of this pointer.  If we fail to read the memory, break.  The
        //        // stack region should be in the crash dump.
        //        if (!dataTarget.DataReader.ReadPointer(ptr, out ulong obj))
        //            break;

        //        // 003DF2A4 
        //        // We check to see if this address is a valid object by simply calling
        //        // GetObjectType.  If that returns null, it's not an object.
        //        ClrType type = heap.GetObjectType(obj);
        //        if (type == null)
        //            continue;

        //        // Don't print out free objects as there tends to be a lot of them on
        //        // the stack.
        //        if (!type.IsFree)
        //            log.AppendLine($"{ptr:X} {obj:X} {type.Name}");
        //    }
        //}

        log.AppendLine();
        log.AppendLine("----------------------------------");
        log.AppendLine();
    }
}
Console.WriteLine(log.ToString());
