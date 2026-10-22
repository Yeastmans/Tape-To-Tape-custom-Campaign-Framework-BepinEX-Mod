using System.Reflection;
namespace WrapperFixtures;
// A deliberately duplicated metadata enumeration proves fail-closed handling;
// real CLR types cannot declare duplicate identical method definitions.
sealed class DuplicateMethods : TypeDelegator
{
    internal DuplicateMethods() : base(typeof(Overloads)) { }
    public override MethodInfo[] GetMethods(BindingFlags flags)
    {
        var method = typeof(Overloads).GetMethods(flags).Single(m => m.Name == "Load" && !m.IsGenericMethod);
        return new[] { method, method };
    }
}
class Overloads
{
    public object Load(string key) => null!;
    public T Load<T>(string key) => default!;
}
class GenericOnly { public T Load<T>(string key) => default!; }
class StaticOnly { public static object Load(string key) => null!; }
class CoercibleOnly { public object Load(object key) => null!; }
class InheritedOnly : Overloads { }
