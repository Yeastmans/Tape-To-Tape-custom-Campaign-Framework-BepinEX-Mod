using System;
using System.Linq;
using System.Reflection;

namespace EndlessMode;

// Generated wrappers can expose Load(string) and Load<T>(string) together.
// Resolve only the precise non-generic API that the checkpoint adapter calls;
// binder coercion, inherited alternatives and ambiguous candidates are rejected.
internal static class GoalieWrapperContract
{
    internal static MethodInfo Resolve(Type type, string name, bool isStatic, Type result, params Type[] args)
    {
        var candidates = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == name && method.IsStatic == isStatic && !method.IsGenericMethod
                && method.ReturnType == result && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(args)).ToArray();
        if (candidates.Length != 1)
            throw new MissingMethodException($"Goalie wrapper contract: {type.FullName}.{name} requires exactly one {(isStatic ? "static" : "instance")} non-generic overload; found {candidates.Length}.");
        return candidates[0];
    }
}
