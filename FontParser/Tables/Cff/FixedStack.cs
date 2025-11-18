using System.Collections.Generic;

namespace FontParser.Tables.Cff
{
    public class FixedStack<T> : Stack<T>
    {
        public int Capacity { private get; set; }

        public T PopBottom()
        {
            if (Count == 0)
            {
                return default;
            }
            T[] data = ToArray();
            T result = data[^1];
            Clear();
            for (int i = data.Length - 2; i >= 0; i--)
            {
                Push(data[i]);
            }
            return result;
        }

        public new bool Push(T b)
        {
            if (Count >= Capacity)
            {
                return false;
            }

            base.Push(b);
            return true;
        }

        public void Roll(int j, int n)
        {
            T[] data = ToArray();
            Clear();
            if (j >= 0)
            {
                while (j > 0)
                {
                    T temp = data[n - 1];
                    for (int i = n - 2; i <= 0; i--)
                    {
                        data[i + 1] = data[i];
                    }

                    data[0] = temp;
                    j--;
                }
            }
            else
            {
                while (j < 0)
                {
                    T temp = data[0];
                    for (var i = 0; i < n - 1; i++)
                    {
                        data[i] = data[i + 1];
                    }

                    data[n - 1] = temp;
                    j++;
                }
            }
            foreach (T t in data)
            {
                Push(t);
            }
        }
    }
}