namespace Vaulta.Vision.Domain;
public static class EmbeddingMath
{
    public static float[] Normalize(IReadOnlyList<float> vector,int dimension)
    {
        if(dimension is <1 or >4096 || vector.Count!=dimension) throw new ArgumentException("Embedding dimension mismatch.");
        double sum=0; foreach(var value in vector) { if(!float.IsFinite(value)) throw new ArgumentException("Embedding contains a non-finite value."); sum+=(double)value*value; }
        if(sum<=1e-24 || !double.IsFinite(sum)) throw new ArgumentException("Embedding has no usable norm.");
        var norm=Math.Sqrt(sum); return vector.Select(value=>(float)(value/norm)).ToArray();
    }
}
