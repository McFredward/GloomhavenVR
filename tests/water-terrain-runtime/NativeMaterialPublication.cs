using System.Linq;
using UnityEngine;

// Exact original method extracted from GH.Runtime MaterialLoaderData. Addressable
// completion timing and loader storage are boundary inputs; its native material
// assignment/re-enable method and production Harmony postfix execute unchanged.
public sealed class MaterialLoaderData
{
    public Renderer Renderer = null!;
    public bool IsSaveExistedMaterials = false;
    private Material[] _loadedMaterials = System.Array.Empty<Material>();
    public void Complete(Renderer renderer, Material[] loaded)
    {
        Renderer = renderer; _loadedMaterials = loaded; CheckAllMaterialLoaded();
    }
	private void CheckAllMaterialLoaded()
	{
		if (_loadedMaterials.Any((Material m) => m == null))
		{
			return;
		}
		if (IsSaveExistedMaterials)
		{
			for (int i = 0; i < Renderer.sharedMaterials.Length; i++)
			{
				if (Renderer.sharedMaterials[i] != null)
				{
					_loadedMaterials[i] = Renderer.sharedMaterials[i];
				}
			}
		}
		Renderer.sharedMaterials = _loadedMaterials;
		Renderer.enabled = true;
	}
}
