using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using MQTTnet.Client;

namespace A_Rigator_Controller.Services;

/// <summary>
/// Validates MQTT server certificates against the Mozilla NSS root set shipped
/// with the app. Hostname validation remains mandatory; this is not a
/// certificate-pinning or trust-all callback.
/// </summary>
internal static class MozillaRootCertificateValidator
{
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
    private static readonly Lazy<X509Certificate2Collection> TrustedRoots = new(LoadTrustedRoots);

    public static bool Validate(MqttClientCertificateValidationEventArgs eventArgs)
    {
        // Accept the platform's normal successful validation without changing
        // its behavior.
        if (eventArgs.SslPolicyErrors == System.Net.Security.SslPolicyErrors.None)
            return true;

        // Only a chain-trust failure can be reconsidered against Mozilla roots.
        // Name mismatch and missing-certificate errors are never overridden.
        if (eventArgs.SslPolicyErrors != System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors)
            return false;

        try
        {
            using var leaf = eventArgs.Certificate is X509Certificate2 certificate2
                ? new X509Certificate2(certificate2.RawData)
                : new X509Certificate2(eventArgs.Certificate);

            using var chain = new X509Chain();
            var policy = chain.ChainPolicy;
            policy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            policy.CustomTrustStore.AddRange(TrustedRoots.Value);
            policy.RevocationMode = X509RevocationMode.NoCheck;
            policy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            policy.VerificationFlags = X509VerificationFlags.NoFlag;
            policy.ApplicationPolicy.Add(new Oid(ServerAuthenticationOid));

            // Android may have built a partial chain using its own system roots.
            // Reuse any presented intermediates, but trust only the Mozilla roots
            // loaded into CustomTrustStore.
            if (eventArgs.Chain is not null)
            {
                foreach (var element in eventArgs.Chain.ChainElements)
                {
                    if (!element.Certificate.RawData.AsSpan().SequenceEqual(leaf.RawData))
                        policy.ExtraStore.Add(element.Certificate);
                }
            }

            var trusted = chain.Build(leaf);
            if (!trusted)
            {
                var status = string.Join(", ", chain.ChainStatus.Select(s => s.StatusInformation.Trim()));
                System.Diagnostics.Debug.WriteLine($"[MQTT TLS] Mozilla root validation failed: {status}");
            }

            return trusted;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MQTT TLS] Mozilla root validation exception: {ex}");
            return false;
        }
    }

    private static X509Certificate2Collection LoadTrustedRoots()
    {
        const string resourceName = "A_Rigator_Controller.Resources.Raw.cacert.pem";
        using var stream = typeof(MozillaRootCertificateValidator).Assembly
            .GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded Mozilla CA bundle '{resourceName}' was not found.");

        using var reader = new StreamReader(stream);
        var pem = reader.ReadToEnd();
        var certificates = Regex.Matches(
            pem,
            "-----BEGIN CERTIFICATE-----\\s+.*?-----END CERTIFICATE-----",
            RegexOptions.Singleline);

        var roots = new X509Certificate2Collection();
        foreach (Match match in certificates)
            roots.Add(X509Certificate2.CreateFromPem(match.Value));

        if (roots.Count == 0)
            throw new CryptographicException("The embedded Mozilla CA bundle contains no certificates.");

        System.Diagnostics.Debug.WriteLine($"[MQTT TLS] Loaded {roots.Count} Mozilla CA roots (certifi 2026.10.23).");
        return roots;
    }
}
