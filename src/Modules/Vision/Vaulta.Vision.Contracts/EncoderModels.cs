namespace Vaulta.Vision.Contracts;
public sealed record EncoderIdentity(string ModelId,string Revision,string WeightsSha256,string PreprocessingVersion,int Dimension,string Runtime,string InputTensor,string OutputTensor,string PreprocessingParametersSha256="");
public sealed record ImageEmbedding(EncoderIdentity Identity,float[] Vector);
public sealed record VisualMatch(Guid ReferenceId,Guid PrintingId,double Similarity,string Origin);
public sealed record VisualIndexEntry(Guid ReferenceId,Guid PrintingId,float[] Vector,string Origin);
public sealed record VisualIndexStatus(string Version,int ReferenceCount,EncoderIdentity Identity);
