using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Unity.MemoryProfiler.Tests")]
namespace Unity.MemoryProfiler
{
    internal class ReflectionUtility
    {
        public static List<Type> GetTypesImplementingInterfaceFromCurrentDomain(Type baseType)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            List<Type> derived = new List<Type>();

            for (int i = 0; i < assemblies.Length; ++i)
            {
                Type[] types = assemblies[i].GetTypes();

                for (int j = 0; j < types.Length; ++j)
                {
                    Type type = types[j];
                    if (!type.IsAbstract)
                    {
                        Type[] interfaces = type.GetInterfaces();
                        for (int k = 0; k < interfaces.Length; ++k)
                        {
                            if (interfaces[k] == baseType)
                            {
                                derived.Add(type);
                                break;
                            }
                        }
                    }
                }
            }
            return derived;
        }
    }
}
