using System;
using System.Reflection;
using System.Linq;

public class Program {
    public static void Main() {
        var dllPath = "/var/home/maxfridbe/.nuget/packages/terminal.gui/2.0.0-develop.4737/lib/net8.0/Terminal.Gui.dll";
        var assembly = Assembly.LoadFrom(dllPath);
        
        var types = assembly.GetTypes();
        var cbType = types.FirstOrDefault(t => t.Name == "ComboBox" && t.Namespace == "Terminal.Gui.Views");
        if (cbType != null) {
            Console.WriteLine($"\nMembers of {cbType.FullName}:");
            foreach (var prop in cbType.GetProperties()) {
                Console.WriteLine($"Property: {prop.Name} ({prop.PropertyType})");
            }
        }
    }
}