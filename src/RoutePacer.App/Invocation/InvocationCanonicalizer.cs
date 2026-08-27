using System.Text;
namespace RoutePacer.App.Invocation;

public static class InvocationCanonicalizer
{
    public static byte[] GetBytes(InvocationRequest request) => Encoding.UTF8.GetBytes($"rt\n1\n{request.PayloadUri.AbsoluteUri}\n{request.Name}\n{request.IssuedUnixMilliseconds}");
}
