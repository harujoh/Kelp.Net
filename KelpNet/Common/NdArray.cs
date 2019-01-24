﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace KelpNet
{
    [Serializable]
    [DebuggerDisplay("{Name + ToString(\"Size\")}", Type = "{\"NdArray\" + ToString(\"Size\")}")]
    public unsafe class NdArray<T> : IDisposable where T : unmanaged, IComparable<T>
    {
        public string Name;
        
        public Real<T>[] Data;

        [NonSerialized]
        public Real<T>[] Grad;

        //このNdArrayの各次元のサイズ
        public int[] Shape { private set; get; }

        //Shapeから算出されるLengthで、DataのLengthとは異なる
        public int Length { private set; get; }

        //関数によって使用された回数をカウントしBackward動作のタイミングを図る
        [NonSerialized]
        public int UseCount;

        //自身が関数から生成された場合、その関数をここに保存する
        [NonSerialized]
        public Function<T> ParentFunc;

        //各関数内でまとめて実行されるバッチの数を示し、Loss関数内の割引で使用される
        public int BatchCount;

        //Updateを行わずに実行されたBackwardの回数をカウントし、Optimizer実行時に使用する
        [NonSerialized]
        public int TrainCount;

        public NdArray(Array data, Function<T> parentFunc = null)
        {
            this.Name = "NdArray";
            this.UseCount = 0;

            Real<T>[] resultData = Real<T>.GetArray(data);

            int[] resultShape = new int[data.Rank];

            for (int i = 0; i < data.Rank; i++)
            {
                resultShape[i] = data.GetLength(i);
            }

            this.Data = resultData;
            this.Shape = resultShape;
            this.Length = Data.Length;
            this.Grad = new Real<T>[this.Length];
            this.BatchCount = 1;
            this.TrainCount = 0;
            this.ParentFunc = parentFunc;
        }

        public NdArray(params int[] shape)
        {
            this.Name = "NdArray";
            this.UseCount = 0;
            this.ParentFunc = null;

            this.Data = new Real<T>[ShapeToArrayLength(shape)];
            this.Shape = shape.ToArray();
            this.Length = Data.Length;
            this.BatchCount = 1;
            this.Grad = new Real<T>[this.Length];
            this.TrainCount = 0;
        }

        public NdArray(Real<T>[] data, int[] shape, int batchCount = 1, Function<T> parentFunc = null)
        {
            this.Name = "NdArray";
            this.UseCount = 0;

            this.Shape = shape.ToArray();
            this.Length = data.Length / batchCount; ;
            this.BatchCount = batchCount;
            this.Data = data.ToArray();
            this.Grad = new Real<T>[this.Length * batchCount];
            this.TrainCount = 0;
            this.ParentFunc = parentFunc;
        }

        public NdArray(int[] shape, int batchCount, Function<T> parentFunc = null)
        {
            this.Name = "NdArray";
            this.UseCount = 0;

            this.Shape = shape.ToArray();
            this.Length = ShapeToArrayLength(this.Shape);
            this.BatchCount = batchCount;
            this.Data = new Real<T>[this.Length * batchCount];
            this.Grad = new Real<T>[this.Length * batchCount];
            this.TrainCount = 0;
            this.ParentFunc = parentFunc;
        }

        //アレイ配列をバッチとして登録する
        public static NdArray<T> FromArrays(Array[] arrays, Function<T> parentFunc = null)
        {
            int[] resultShape = new int[arrays[0].Rank];

            for (int i = 0; i < arrays[0].Rank; i++)
            {
                resultShape[i] = arrays[0].GetLength(i);
            }

            int length = arrays[0].Length;
            Real<T>[] result = new Real<T>[length * arrays.Length];

            for (int i = 0; i < arrays.Length; i++)
            {
                Array.Copy(Real<T>.GetArray(arrays[i]), 0, result, length * i, length);
            }

            return new NdArray<T>(result, resultShape, arrays.Length, parentFunc);
        }

        public static NdArray<T> Convert(Real<T>[] data, int[] shape, int batchCount, Function<T> parentFunc = null)
        {
            return new NdArray<T>(shape, batchCount, parentFunc) { Data = data };
        }

        public static NdArray<T> ZerosLike(NdArray<T> baseArray)
        {
            return new NdArray<T>(baseArray.Shape, baseArray.BatchCount);
        }

        //インデクサはあまり早くないので頻繁にアクセスする場合は使用をオススメしません。デバッグ用途向けと割り切ってください。
        public Real<T> this[int batchcount, params int[] indices]
        {
            get
            {
                return this.Data[this.GetLocalIndex(batchcount, indices)];
            }
            set
            {
                this.Data[this.GetLocalIndex(batchcount, indices)] = value;
            }
        }


        public void Reshape(params int[] shape)
        {
            int val = 0;
            int dimension = Length;

            //-1指定を算出
            if (shape.Contains(-1))
            {
                int minusIndex = -1;

                for (int i = 0; i < shape.Length; i++)
                {
                    if (shape[i] != -1)
                    {
                        val += Length % shape[i];

                        if (val == 0)
                        {
                            dimension /= shape[i];
                        }
                        else
                        {
                            throw new Exception("要素の指定が間違っています");
                        }
                    }
                    else
                    {
                        if (minusIndex != -1)
                        {
                            throw new Exception("-1が二つ以上指定されています");
                        }

                        minusIndex = i;
                    }
                }

                shape[minusIndex] = dimension;
            }
#if DEBUG
            else if (Length != ShapeToArrayLength(shape)) throw new Exception("指定されたShapeのサイズが現在のData.Lengthと等しくありません");
#endif

            Shape = shape.ToArray();
        }

        //バッチでまとまっているアレイをバラバラにして排出する
        public NdArray<T>[] DivideArrays()
        {
            NdArray<T>[] result = new NdArray<T>[BatchCount];

            for (int i = 0; i < result.Length; i++)
            {
                result[i] = GetSingleArray(i);
            }

            return result;
        }

        //バッチ番号に対応するアレイを排出する
        public NdArray<T> GetSingleArray(int i)
        {
            Real<T>[] data = new Real<T>[this.Length];
            Array.Copy(this.Data, i * this.Length, data, 0, this.Length);

            return new NdArray<T>(data, this.Shape);
        }

        static int ShapeToArrayLength(params int[] shapes)
        {
            int result = 1;

            foreach (int shape in shapes)
            {
                result *= shape;
            }

            return result;
        }

        public void Backward()
        {
            if (ParentFunc != null)
            {
                for (int i = 0; i < Grad.Length; i++)
                {
                    Grad[i] = 1;
                }

                NdArray<T>.Backward(this);
            }
        }

        public static void Backward(NdArray<T> y)
        {
            if (y.ParentFunc != null)
            {
                List<NdArray<T>[]> prevInputs = y.ParentFunc.PrevInputs;
                NdArray<T>[] xs = prevInputs[prevInputs.Count - 1];

                y.ParentFunc.Backward(y);

                for (int i = 0; i < xs.Length; i++)
                {
                    if (xs[i].UseCount == 0)
                    {
                        NdArray<T>.Backward(xs[i]);
                    }
                }
            }
        }

        public void CountUp()
        {
            TrainCount++;
        }

        //傾きの補正
        public bool Reduce()
        {
            if (this.TrainCount > 0)
            {
                for (int i = 0; i < this.Grad.Length; i++)
                {
                    this.Grad[i] /= this.TrainCount;
                }

                return true;
            }

            return false;
        }

        //傾きの初期化
        public void ClearGrad()
        {
            this.Grad = new Real<T>[this.Data.Length];

            //カウンタをリセット
            this.TrainCount = 0;
        }

        public override string ToString()
        {
            return ToString(this.Data);
        }

        public string ToString(string format)
        {
            switch (format)
            {
                case "Data":
                    return ToString(this.Data);

                case "Grad":
                    return ToString(this.Grad);

                case "Shape":
                    return "[" + string.Join(",", Shape) + "]";

                case "Size":
                    return "[" + string.Join(",", Shape) + "]" +
                           (BatchCount > 1 ? "x" + BatchCount + "batch" : string.Empty);

                case "Name":
                    return Name;

                default:
                    return Name;
            }
        }

        public string ToString(Real<T>[] datas)
        {
            StringBuilder sb = new StringBuilder();

            int intMaxLength = 0; //整数部の最大値
            int realMaxLength = 0; //小数点以下の最大値
            bool isExponential = false; //指数表現にするか

            foreach (Real<T> data in datas)
            {
                string[] divStr = data.ToString().Split('.');
                intMaxLength = Math.Max(intMaxLength, divStr[0].Length);

                if (divStr.Length > 1)
                {
                    isExponential |= divStr[1].Contains("E");
                }

                if (realMaxLength != 8 && divStr.Length == 2)
                {
                    realMaxLength = Math.Max(realMaxLength, divStr[1].Length);

                    if (realMaxLength > 8)
                    {
                        realMaxLength = 8;
                    }
                }
            }

            //配列の約数を取得
            int[] commonDivisorList = new int[this.Shape.Length];

            //一個目は手動取得
            commonDivisorList[0] = this.Shape[this.Shape.Length - 1];

            for (int i = 1; i < this.Shape.Length; i++)
            {
                commonDivisorList[i] = commonDivisorList[i - 1] * this.Shape[this.Shape.Length - i - 1];
            }

            if (this.BatchCount > 1)
            {
                sb.Append("{");
            }

            for (int batch = 0; batch < this.BatchCount; batch++)
            {
                int indexOffset = batch * Length;
                //先頭の括弧
                for (int i = 0; i < this.Shape.Length; i++)
                {
                    sb.Append("[");
                }

                int closer = 0;
                for (int i = 0; i < Length; i++)
                {
                    string[] divStr;
                    if (isExponential)
                    {
                        divStr = string.Format("{0:0.00000000e+00}", datas[indexOffset + i]).Split('.');
                    }
                    else
                    {
                        divStr = (datas[indexOffset + i]).ToString().Split('.');
                    }

                    //最大文字数でインデントを揃える
                    for (int j = 0; j < intMaxLength - divStr[0].Length; j++)
                    {
                        sb.Append(" ");
                    }
                    sb.Append(divStr[0]);
                    if (realMaxLength != 0)
                    {
                        sb.Append(".");
                    }
                    if (divStr.Length == 2)
                    {
                        sb.Append(divStr[1].Length > 8 && !isExponential ? divStr[1].Substring(0, 8) : divStr[1]);
                        for (int j = 0; j < realMaxLength - divStr[1].Length; j++)
                        {
                            sb.Append(" ");
                        }
                    }
                    else
                    {
                        for (int j = 0; j < realMaxLength; j++)
                        {
                            sb.Append(" ");
                        }
                    }

                    //約数を調査してピッタリなら括弧を出力
                    if (i != Length - 1)
                    {
                        foreach (int commonDivisor in commonDivisorList)
                        {
                            if ((i + 1) % commonDivisor == 0)
                            {
                                sb.Append("]");
                                closer++;
                            }
                        }

                        sb.Append(" ");

                        if ((i + 1) % commonDivisorList[0] == 0)
                        {
                            //閉じ括弧分だけ改行を追加
                            for (int j = 0; j < closer; j++)
                            {
                                sb.Append("\n");
                            }
                            closer = 0;

                            if (BatchCount > 1) sb.Append(" ");

                            //括弧前のインデント
                            foreach (int commonDivisor in commonDivisorList)
                            {
                                if ((i + 1) % commonDivisor != 0)
                                {
                                    sb.Append(" ");
                                }
                            }
                        }

                        foreach (int commonDivisor in commonDivisorList)
                        {
                            if ((i + 1) % commonDivisor == 0)
                            {
                                sb.Append("[");
                            }
                        }
                    }
                }

                //後端の括弧
                for (int i = 0; i < this.Shape.Length; i++)
                {
                    sb.Append("]");
                }

                if (batch < this.BatchCount - 1)
                {
                    sb.Append("},\n{");
                }
            }

            if (this.BatchCount > 1)
            {
                sb.Append("}");
            }

            return sb.ToString();
        }


        public static NdArray<T> operator +(NdArray<T> a, NdArray<T> b)
        {
            return new Add<T>().Forward(a, b)[0];
        }

        public static NdArray<T> operator +(NdArray<T> a, Real<T> b)
        {
            return new AddConst<T>().Forward(a, b)[0];
        }

        public static NdArray<T> operator +(Real<T> a, NdArray<T> b)
        {
            return new AddConst<T>().Forward(b, a)[0];
        }


        public static NdArray<T> operator *(NdArray<T> a, NdArray<T> b)
        {
            return new Mul<T>().Forward(a, b)[0];
        }

        public static NdArray<T> operator *(NdArray<T> a, Real<T> b)
        {
            return new MulConst<T>().Forward(a, b)[0];
        }

        public static NdArray<T> operator *(Real<T> a, NdArray<T> b)
        {
            return new MulConst<T>().Forward(b, a)[0];
        }


        public static NdArray<T> operator -(NdArray<T> a, NdArray<T> b)
        {
            return new Sub<T>().Forward(a, b)[0];
        }

        public static NdArray<T> operator -(NdArray<T> a, Real<T> b)
        {
            return new SubConst<T>().Forward(a, b)[0];
        }

        public static NdArray<T> operator -(Real<T> a, NdArray<T> b)
        {
            return new ConstSub<T>().Forward(a, b)[0];
        }


        public static NdArray<T> operator /(NdArray<T> a, NdArray<T> b)
        {
            return new Div<T>().Forward(a, b)[0];
        }

        public static NdArray<T> operator /(NdArray<T> a, Real<T> b)
        {
            return new DivConst<T>().Forward(a, b)[0];
        }

        public static NdArray<T> operator /(Real<T> a, NdArray<T> b)
        {
            return new ConstDiv<T>().Forward(a, b)[0];
        }

        public static implicit operator NdArray<T>(Real<T>[] a)
        {
            return new NdArray<T>(a);
        }

        public static implicit operator NdArray<T>(Real<T> a)
        {
            return new NdArray<T>(new[] { a });
        }

        //public static implicit operator NdArray<T>(long a)
        //{
        //    return new NdArray<T>(new[] { (Real<T>)a });
        //}

        //コピーを作成するメソッド
        public NdArray<T> Clone()
        {
            return new NdArray<T>(Data, Shape, BatchCount, ParentFunc)
            {
                Grad = Grad.ToArray(),
                Name = Name,
                Length = Length,
                UseCount = UseCount,
                TrainCount = TrainCount
            };
        }

        public static NdArray<T> Sum(NdArray<T> a, bool keepDims = false, params int[] axis)
        {
#if DEBUG
            if (axis.Length != axis.Distinct().ToArray().Length)
            {
                throw new Exception("指定された要素が重複しています");
            }

            if (axis.Length != 0 && a.Shape.Length < axis.Max())
            {
                throw new Exception("指定された要素が範囲を超えています");
            }
#endif
            if (axis.Length == 0)
            {
                axis = Enumerable.Range(0, a.Shape.Length).ToArray();
            }

            Array.Sort(axis);

            NdArray<T> result = Sum(a, axis[0]);

            for (int i = 1; i < axis.Length; i++)
            {
                result = Sum(result, axis[i] - i);
            }

            if (keepDims)
            {
                List<int> resultKeepDimShape = new List<int>();
                int count = a.Shape.Length - result.Shape.Length;

                for (int i = 0; i < count; i++)
                {
                    resultKeepDimShape.Add(1);
                }

                resultKeepDimShape.AddRange(result.Shape);
                result.Shape = resultKeepDimShape.ToArray();
            }

            return result;
        }

        private static NdArray<T> Sum(NdArray<T> a, int axis)
        {
            int[] resultShape = new int[a.Shape.Length - 1];

            for (int i = 0, j = 0; i < a.Shape.Length; i++)
            {
                if (i != axis)
                {
                    resultShape[j++] = a.Shape[i];
                }
            }

            NdArray<T> result = new NdArray<T>(resultShape, a.BatchCount);

            for (int i = 0; i < a.Length; i++)
            {
                List<int> index = new List<int>(a.GetDimensionsIndex(i));
                index.RemoveAt(axis);
                int localIndex = result.GetLocalIndex(0, index.ToArray());

                for (int batchCount = 0; batchCount < a.BatchCount; batchCount++)
                {
                    result.Data[batchCount * result.Length + localIndex] += a.Data[batchCount * a.Length + i];
                    result.Grad[batchCount * result.Length + localIndex] += a.Grad[batchCount * a.Length + i];
                }
            }

            return result;
        }

        public static NdArray<T>[] Split(NdArray<T> array, int indices, int axis = 1)
        {
            return Split(array, new[] { indices }, axis);
        }

        public static NdArray<T>[] Split(NdArray<T> array, int[] indices, int axis = 1)
        {
            int[] shapeOffets = new int[indices.Length + 1];        //入力されたindicesの先頭0を追加した配列
            int[] resultAxisShapes = new int[indices.Length + 1];   //分割後の指定軸のShape

            for (int i = 0; i < indices.Length; i++)
            {
                shapeOffets[i + 1] = indices[i];
                resultAxisShapes[i] = indices[i] - shapeOffets[i];
            }
            resultAxisShapes[indices.Length] = array.Shape[axis] - indices[indices.Length - 1];

            NdArray<T>[] resultArrays = new NdArray<T>[indices.Length + 1];

            for (int i = 0; i < resultArrays.Length; i++)
            {
                int[] resultShape = array.Shape.ToArray();
                resultShape[axis] = resultAxisShapes[i];
                resultArrays[i] = new NdArray<T>(resultShape, array.BatchCount);
            }

            for (int batchCount = 0; batchCount < array.BatchCount; batchCount++)
            {
                for (int i = 0; i < resultArrays.Length; i++)
                {
                    for (int j = 0; j < resultArrays[i].Length; j++)
                    {
                        int[] resultIndex = resultArrays[i].GetDimensionsIndex(j);
                        resultIndex[axis] += shapeOffets[i];
                        int localIndex = array.GetLocalIndex(batchCount, resultIndex);

                        resultArrays[i].Data[batchCount * resultArrays[i].Length + j] = array.Data[localIndex];
                        resultArrays[i].Grad[batchCount * resultArrays[i].Length + j] = array.Grad[localIndex];
                    }
                }
            }

            return resultArrays;
        }

        public static NdArray<T> Concatenate(NdArray<T> a, NdArray<T> b, int axis)
        {
            int[] shapeList = a.Shape.ToArray();
            shapeList[axis] += b.Shape[axis];

#if DEBUG
            for (int i = 0; i < a.Shape.Length; i++)
            {
                if (i != axis && a.Shape[i] != b.Shape[i])
                {
                    throw new Exception("配列の大きさがマッチしていません");
                }
            }

            if (a.BatchCount != b.BatchCount)
            {
                throw new Exception("バッチの大きさがマッチしていません");
            }
#endif

            NdArray<T> result = new NdArray<T>(shapeList, a.BatchCount);

            for (int batchCount = 0; batchCount < a.BatchCount; batchCount++)
            {
                int aInputBatchoffset = batchCount * a.Length;
                int bInputBatchoffset = batchCount * b.Length;

                for (int i = 0; i < a.Length; i++)
                {
                    int resultindex = result.GetLocalIndex(batchCount, a.GetDimensionsIndex(i));

                    result.Data[resultindex] = a.Data[i + aInputBatchoffset];
                    result.Grad[resultindex] = a.Grad[i + aInputBatchoffset];
                }

                for (int i = 0; i < b.Length; i++)
                {
                    int[] tmpIndex = b.GetDimensionsIndex(i);
                    tmpIndex[axis] += a.Shape[axis];

                    int resultIndex = result.GetLocalIndex(batchCount, tmpIndex);

                    result.Data[resultIndex] = b.Data[i + bInputBatchoffset];
                    result.Grad[resultIndex] = b.Grad[i + bInputBatchoffset];
                }
            }

            return result;
        }

        internal int[] GetDimensionsIndex(int index)
        {
            //バッチ分を補正
            int batchCount = index / this.Length;
            index -= this.Length * batchCount;

            int[] dimensionsIndex = new int[this.Shape.Length];

            for (int i = this.Shape.Length - 1; i >= 0; i--)
            {
                dimensionsIndex[i] = index % this.Shape[i];
                index /= this.Shape[i];
            }

            return dimensionsIndex;
        }

        internal int GetLocalIndex(int batchIndex, params int[] indices)
        {
            int indicesLastIndex = indices.Length - 1;
            int index = batchIndex * this.Length + indices[indicesLastIndex];
            int rankoffset = 1;

            for (int i = 1; i < indices.Length; i++)
            {
                rankoffset *= this.Shape[indicesLastIndex--];
                index += indices[indicesLastIndex] * rankoffset;
            }

            return index;
        }

        public void Dispose()
        {
        }
    }
}