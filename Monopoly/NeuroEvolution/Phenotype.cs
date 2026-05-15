using System;
using System.Collections.Generic;

namespace NEAT
{
    public class Vertex
    {
        public enum EType
        {
            INPUT = 0,
            HIDDEN = 1,
            OUTPUT = 2,
        }

        public EType type;
        public int index = 0;

        public List<Edge> incoming;

        public Vertex(EType t, int i)
        {
            type = t;
            index = i;

            incoming = new List<Edge>();
        }
    }

    public class Edge
    {
        public enum EType
        {
            FORWARD,
            RECURRENT,
        }

        public EType type = EType.FORWARD;
        public int source = 0;
        public int destination = 0;

        public float weight = 0.0f;
        public bool enabled = true;

        public Edge(int s, int d, float w, bool e)
        {
            source = s;
            destination = d;

            weight = w;
            enabled = e;
        }
    }

    // Phenotype is the immutable, executable form of a Genotype.
    // It holds the network topology and weights; per-call activation state
    // lives in a workspace allocated inside Propagate, so multiple threads
    // can safely share a single Phenotype.
    public class Phenotype
    {
        public List<Vertex> vertices;
        public List<Edge> edges;

        public List<Vertex> vertices_inputs;
        public List<Vertex> vertices_outputs;

        // Positional indices into `vertices` for the input and output vertices.
        // Edge.source / Edge.destination are used as direct indices into the
        // activation workspace, so input/output reads/writes need the same scheme.
        public List<int> input_positions;
        public List<int> output_positions;

        public float score = 0;

        public Phenotype()
        {
            vertices = new List<Vertex>();
            edges = new List<Edge>();

            vertices_inputs = new List<Vertex>();
            vertices_outputs = new List<Vertex>();

            input_positions = new List<int>();
            output_positions = new List<int>();
        }

        public void InscribeGenotype(Genotype code)
        {
            vertices.Clear();
            edges.Clear();

            int vertexCount = code.vertices.Count;
            int edgeCount = code.edges.Count;

            for (int i = 0; i < vertexCount; i++)
            {
                AddVertex((Vertex.EType)(int)code.vertices[i].type, code.vertices[i].index);
            }

            for (int i = 0; i < edgeCount; i++)
            {
                AddEdge(code.edges[i].source, code.edges[i].destination, code.edges[i].weight, code.edges[i].enabled);
            }
        }

        public void AddVertex(Vertex.EType type, int index)
        {
            Vertex v = new Vertex(type, index);
            vertices.Add(v);
        }

        public void AddEdge(int source, int destination, float weight, bool enabled)
        {
            Edge e = new Edge(source, destination, weight, enabled);
            edges.Add(e);

            vertices[e.destination].incoming.Add(e);
        }

        public void ProcessGraph()
        {
            input_positions.Clear();
            output_positions.Clear();
            vertices_inputs.Clear();
            vertices_outputs.Clear();

            int verticesCount = vertices.Count;

            for (int i = 0; i < verticesCount; i++)
            {
                Vertex vertex = vertices[i];

                if (vertex.type == Vertex.EType.INPUT)
                {
                    vertices_inputs.Add(vertex);
                    input_positions.Add(i);
                }
                else if (vertex.type == Vertex.EType.OUTPUT)
                {
                    vertices_outputs.Add(vertex);
                    output_positions.Add(i);
                }
            }
        }

        public float[] Propagate(float[] X)
        {
            int repeats = 10;
            int verticesCount = vertices.Count;
            int outputsCount = vertices_outputs.Count;
            int inputsCount = vertices_inputs.Count;

            float[] values = new float[verticesCount];

            for (int e = 0; e < repeats; e++)
            {
                for (int i = 0; i < inputsCount; i++)
                {
                    values[input_positions[i]] = X[i];
                }

                for (int i = 0; i < verticesCount; i++)
                {
                    if (vertices[i].type == Vertex.EType.OUTPUT)
                    {
                        continue;
                    }

                    int paths = vertices[i].incoming.Count;

                    for (int j = 0; j < paths; j++)
                    {
                        Edge edge = vertices[i].incoming[j];
                        values[i] += values[edge.source] * edge.weight * (edge.enabled ? 1.0f : 0.0f);
                    }

                    if (paths > 0)
                    {
                        values[i] = Sigmoid(values[i]);
                    }
                }

                float[] Y = new float[outputsCount];

                for (int i = 0; i < outputsCount; i++)
                {
                    int outPos = output_positions[i];
                    int paths = vertices_outputs[i].incoming.Count;

                    for (int j = 0; j < paths; j++)
                    {
                        Edge edge = vertices_outputs[i].incoming[j];
                        values[outPos] += values[edge.source] * edge.weight * (edge.enabled ? 1.0f : 0.0f);
                    }

                    if (paths > 0)
                    {
                        values[outPos] = Sigmoid(values[outPos]);
                        Y[i] = values[outPos];
                    }
                }

                if (e == repeats - 1)
                {
                    return Y;
                }
            }

            return new float[0];
        }

        public float Sigmoid(float x)
        {
            return 1.0f / (1.0f + (float)Math.Pow(Math.E, -1.0f * x));
        }
    }
}
