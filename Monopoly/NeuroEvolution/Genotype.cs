using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NEAT
{
    // Single source of truth for the on-disk delimiters. Population.Save and
    // .Load consume these so the file layout is one place to look.
    public static class SerialDelim
    {
        public const char MAIN = ';';
        public const char COMMA = ',';
        public const char SPECIES = '&';
        public const char MEMBER = 'n';
        public const char NODE_EDGE = '#';
    }

    public class VertexInfo
    {
        public enum EType
        {
            INPUT = 0,
            HIDDEN = 1,
            OUTPUT = 2,
        }

        public EType type;
        public int index = 0;

        public VertexInfo(EType t, int i)
        {
            type = t;
            index = i;
        }
    }

    public class EdgeInfo
    {
        //structual information
        public int source = 0;
        public int destination = 0;

        //network information
        public float weight = 0.0f;
        public bool enabled = false;
        public int innovation = 0;

        public EdgeInfo(int s, int d, float w, bool e)
        {
            source = s;
            destination = d;

            weight = w;
            enabled = e;
        }
    }

    public class Genotype
    {
        public List<VertexInfo> vertices;
        public List<EdgeInfo> edges;

        public int inputs = 0;
        public int externals = 0;

        public int bracket = 0;

        public float fitness = 0.0f;
        public float adjustedFitness = 0.0f;
            
        public Genotype()
        {
            vertices = new List<VertexInfo>();
            edges = new List<EdgeInfo>();
        }

        public void AddVertex(VertexInfo.EType type, int index)
        {
            VertexInfo v = new VertexInfo(type, index);
            vertices.Add(v);

            if (v.type != VertexInfo.EType.HIDDEN)
            {
                externals++;
            }

            if (v.type == VertexInfo.EType.INPUT)
            {
                inputs++;
            }
        }

        public void AddEdge(int source, int destination, float weight, bool enabled)
        {
            EdgeInfo e = new EdgeInfo(source, destination, weight, enabled);
            edges.Add(e);
        }

        public void AddEdge(int source, int destination, float weight, bool enabled, int innovation)
        {
            EdgeInfo e = new EdgeInfo(source, destination, weight, enabled);
            e.innovation = innovation;
            edges.Add(e);
        }

        public Genotype Clone()
        {
            Genotype copy = new Genotype();

            int vertexCount = vertices.Count;

            for (int i = 0; i < vertexCount; i++)
            {
                copy.AddVertex(vertices[i].type, vertices[i].index);
            }

            int edgeCount = edges.Count;

            for (int i = 0; i < edgeCount; i++)
            {
                copy.AddEdge(edges[i].source, edges[i].destination, edges[i].weight, edges[i].enabled, edges[i].innovation);
            }

            return copy;
        }

        public void SortTopology()
        {
            SortVertices();
            SortEdges();
        }

        public void SortVertices()
        {
            vertices.Sort(CompareVertexByOrder);
        }

        public void SortEdges()
        {
            edges.Sort(CompareEdgeByInnovation);
        }

        public int CompareVertexByOrder(VertexInfo a, VertexInfo b)
        {
            if (a.index > b.index)
            {
                return 1;
            }
            else if (a.index == b.index)
            {
                return 0;
            }

            return -1;
        }

        public int CompareEdgeByInnovation(EdgeInfo a, EdgeInfo b)
        {
            if (a.innovation > b.innovation)
            {
                return 1;
            }
            else if (a.innovation == b.innovation)
            {
                return 0;
            }

            return -1;
        }

        // Serialize this genotype to the on-disk format: each vertex written
        // as "index,type," (trailing comma always), the literal '#', then
        // each edge written as "source,destination,weight,enabled,innovation,"
        // (trailing comma always). The format is byte-identical to what the
        // original Program.SaveState produced.
        public void WriteTo(StringBuilder sb)
        {
            int vertexCount = vertices.Count;
            for (int k = 0; k < vertexCount; k++)
            {
                sb.Append(vertices[k].index);
                sb.Append(SerialDelim.COMMA);
                sb.Append(vertices[k].type.ToString());
                sb.Append(SerialDelim.COMMA);
            }

            sb.Append(SerialDelim.NODE_EDGE);

            int edgeCount = edges.Count;
            for (int k = 0; k < edgeCount; k++)
            {
                sb.Append(edges[k].source);
                sb.Append(SerialDelim.COMMA);
                sb.Append(edges[k].destination);
                sb.Append(SerialDelim.COMMA);
                sb.Append(edges[k].weight);
                sb.Append(SerialDelim.COMMA);
                sb.Append(edges[k].enabled);
                sb.Append(SerialDelim.COMMA);
                sb.Append(edges[k].innovation);
                sb.Append(SerialDelim.COMMA);
            }
        }

        // Parse one genotype from its serialized text (one member's worth of
        // bytes, between two 'n' separators inside a species block).
        public static Genotype Parse(string text)
        {
            Genotype genotype = new Genotype();

            string[] nparts = text.Split(SerialDelim.NODE_EDGE);

            string[] vparts = nparts[0].Split(SerialDelim.COMMA);
            for (int j = 0; j < vparts.GetLength(0) - 1; j += 2)
            {
                int index = int.Parse(vparts[j]);
                VertexInfo.EType type = (VertexInfo.EType)Enum.Parse(typeof(VertexInfo.EType), vparts[j + 1]);
                genotype.AddVertex(type, index);
            }

            string[] eparts = nparts[1].Split(SerialDelim.COMMA);
            for (int j = 0; j < eparts.GetLength(0) - 1; j += 5)
            {
                int source = int.Parse(eparts[j]);
                int destination = int.Parse(eparts[j + 1]);
                float weight = float.Parse(eparts[j + 2]);
                bool enabled = bool.Parse(eparts[j + 3]);
                int innovation = int.Parse(eparts[j + 4]);
                genotype.AddEdge(source, destination, weight, enabled, innovation);
            }

            return genotype;
        }
    }
}
