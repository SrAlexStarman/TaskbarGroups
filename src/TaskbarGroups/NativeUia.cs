using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace TaskbarGroups;

// Native UIA3 includes the XAML providers used by the Windows 11 taskbar.
sealed class NativeUia : IDisposable {
    IntPtr client,condition;
    [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid cls,IntPtr outer,uint context,ref Guid iid,out IntPtr obj);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int PointerOut(IntPtr self,out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FromHandle(IntPtr self,IntPtr hwnd,out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FindAll(IntPtr self,int scope,IntPtr condition,out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Length(IntPtr self,out int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Item(IntPtr self,int index,out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Property(IntPtr self,int id,[MarshalAs(UnmanagedType.Struct)]out object value);
    static T Method<T>(IntPtr obj,int slot) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj),slot*IntPtr.Size));
    public NativeUia(){var cls=new Guid("E22AD333-B25F-460C-83D0-0581107395C9");var iid=new Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE");Marshal.ThrowExceptionForHR(CoCreateInstance(ref cls,IntPtr.Zero,1,ref iid,out client));Marshal.ThrowExceptionForHR(Method<PointerOut>(client,21)(client,out condition));}
    public List<(string Name,int Type,Rectangle Bounds)> Elements(IntPtr handle){
        var result=new List<(string,int,Rectangle)>();IntPtr root=IntPtr.Zero,array=IntPtr.Zero;
        try {Marshal.ThrowExceptionForHR(Method<FromHandle>(client,6)(client,handle,out root));Marshal.ThrowExceptionForHR(Method<FindAll>(root,6)(root,4,condition,out array));Marshal.ThrowExceptionForHR(Method<Length>(array,3)(array,out int count));
            for(int i=0;i<count;i++){IntPtr element=IntPtr.Zero;try{if(Method<Item>(array,4)(array,i,out element)<0)continue;var get=Method<Property>(element,10);if(get(element,30005,out var name)<0)continue;get(element,30003,out var type);get(element,30001,out var bounds);var r=Rectangle.Empty;if(bounds is double[] b&&b.Length==4)r=new Rectangle((int)b[0],(int)b[1],(int)b[2],(int)b[3]);result.Add((name as string??"",type is int n?n:0,r));}finally{if(element!=IntPtr.Zero)Marshal.Release(element);}}
        }finally{if(array!=IntPtr.Zero)Marshal.Release(array);if(root!=IntPtr.Zero)Marshal.Release(root);}return result;
    }
    public void Dispose(){if(condition!=IntPtr.Zero){Marshal.Release(condition);condition=IntPtr.Zero;}if(client!=IntPtr.Zero){Marshal.Release(client);client=IntPtr.Zero;}}
}
