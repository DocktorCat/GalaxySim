using ComputeSharp;

namespace GalaxySim.Core.Shaders;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct BuildTraversalLinksShader(

    ReadWriteBuffer<int> nodeLeftChild,   
    ReadWriteBuffer<int> nodeRightChild,  
    ReadWriteBuffer<int> nodeParent,      
                                          
    ReadWriteBuffer<int> travLeft,
    ReadWriteBuffer<int> travNext,
    int particleCount) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        int totalNodes = 2 * particleCount - 1;
        if (i >= totalNodes) return;

        int internalCount = particleCount - 1;


        if (i < internalCount)
        {
            int lcRaw = nodeLeftChild[i];
            travLeft[i] = DecodeChild(lcRaw, internalCount);
        }
        else
        {
            travLeft[i] = -1;
        }


        int myEncoded = (i < internalCount)
            ? i
            : EncodeLeaf(i - internalCount);   

        int cur = myEncoded;
        int parent = nodeParent[i];   
        int next = -1;

        int depth = 0;
        while (parent >= 0 && depth < 64)
        {
            if (nodeLeftChild[parent] == cur)
            {
                
                next = DecodeChild(nodeRightChild[parent], internalCount);
                break;
            }
           
            cur = parent;
            parent = nodeParent[parent];
            depth++;
        }

        travNext[i] = next;
    }


    private static int DecodeChild(int encoded, int internalCount)
    {
        if (encoded >= 0) return encoded;
        int leafIdx = -encoded - 1;
        return internalCount + leafIdx;
    }

    private static int EncodeLeaf(int leafIdx) => -(leafIdx + 1);
}