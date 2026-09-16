"""Compile the production factory's ownership methods with minimal native/Unity adapters."""
from pathlib import Path
import sys
root, output = map(Path, sys.argv[1:])
base = root/'src/GloomhavenVR/Cards'
def method(file, signature):
    text = (base/file).read_text()
    start = text.index(signature)
    opening = text.index('{', start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[start:end]
output.write_text('using System.Collections.Generic;\nnamespace GloomhavenVR.Cards {\ninternal partial class VRCardFactory {\n'+method('VRCardFactory.cs', 'internal VRCard GetOrCreate(')+'\n'+method('VRCardFactory.cs', 'internal void ReturnBorrowedFacesBeforeSceneLoad(')+'\n'+method('VRCardFactory.cs', 'internal bool ReattachBorrowedFacesAfterSceneLoad(')+'\n}\ninternal partial class VRCard {\n'+method('VRCard.cs','internal void ReturnBorrowedFaceForSceneLoad(')+'\n}\n}\n')
