namespace SMAP_WPF.Updates;

public static class UpdateTrust
{
    // Alpha-only trust anchor. No private key is distributed. Stable release signing is provisioned separately.
    public static readonly UpdateKey[] Keys =
    [
        new("alpha-20261114", "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE4825SpqT7CpLBYRJeh4R5zXT6Y3nBH/0cTpDrC3Pxch5CDdgkkhMipccWifp8zuEfyyRkVruhvM4MLerMnLD3w==", "alpha")
    ];
}
