﻿using System;
using System.Linq;
using KelpNet.Sample.DataManager;

namespace KelpNet.Sample.Samples
{
    //Decoupled Neural Interfaces using Synthetic GradientsによるMNIST（手書き文字）の学習
    // http://ralo23.hatenablog.com/entry/2016/10/22/233405
    class Sample11<T> where T : unmanaged, IComparable<T>
    {
        //ミニバッチの数
        const int BATCH_DATA_COUNT = 200;

        //一世代あたりの訓練回数
        const int TRAIN_DATA_COUNT = 300; // = 60000 / 20

        //性能評価時のデータ数
        const int TEST_DATA_COUNT = 1000;

        public static void Run()
        {
            //MNISTのデータを用意する
            Console.WriteLine("MNIST Data Loading...");
            MnistData<T> mnistData = new MnistData<T>();


            Console.WriteLine("Training Start...");

            //ネットワークの構成を FunctionStack に書き連ねる
            FunctionStack<T> Layer1 = new FunctionStack<T>(
                new Linear<T>(28 * 28, 256, name: "l1 Linear"),
                new BatchNormalization<T>(256, name: "l1 Norm"),
                new ReLU<T>(name: "l1 ReLU")
            );

            FunctionStack<T> Layer2 = new FunctionStack<T>(
                new Linear<T>(256, 256, name: "l2 Linear"),
                new BatchNormalization<T>(256, name: "l2 Norm"),
                new ReLU<T>(name: "l2 ReLU")
            );

            FunctionStack<T> Layer3 = new FunctionStack<T>(
                new Linear<T>(256, 256, name: "l3 Linear"),
                new BatchNormalization<T>(256, name: "l3 Norm"),
                new ReLU<T>(name: "l3 ReLU")
            );

            FunctionStack<T> Layer4 = new FunctionStack<T>(
                new Linear<T>(256, 10, name: "l4 Linear")
            );

            //FunctionStack自身もFunctionとして積み上げられる
            FunctionStack<T> nn = new FunctionStack<T>
            (
                Layer1,
                Layer2,
                Layer3,
                Layer4
            );

            FunctionStack<T> DNI1 = new FunctionStack<T>(
                new Linear<T>(256, 1024, name: "DNI1 Linear1"),
                new BatchNormalization<T>(1024, name: "DNI1 Nrom1"),
                new ReLU<T>(name: "DNI1 ReLU1"),
                new Linear<T>(1024, 1024, name: "DNI1 Linear2"),
                new BatchNormalization<T>(1024, name: "DNI1 Nrom2"),
                new ReLU<T>(name: "DNI1 ReLU2"),
                new Linear<T>(1024, 256, initialW: new Real<T>[1024, 256], name: "DNI1 Linear3")
            );

            FunctionStack<T> DNI2 = new FunctionStack<T>(
                new Linear<T>(256, 1024, name: "DNI2 Linear1"),
                new BatchNormalization<T>(1024, name: "DNI2 Nrom1"),
                new ReLU<T>(name: "DNI2 ReLU1"),
                new Linear<T>(1024, 1024, name: "DNI2 Linear2"),
                new BatchNormalization<T>(1024, name: "DNI2 Nrom2"),
                new ReLU<T>(name: "DNI2 ReLU2"),
                new Linear<T>(1024, 256, initialW: new Real<T>[1024, 256], name: "DNI2 Linear3")
            );

            FunctionStack<T> DNI3 = new FunctionStack<T>(
                new Linear<T>(256, 1024, name: "DNI3 Linear1"),
                new BatchNormalization<T>(1024, name: "DNI3 Nrom1"),
                new ReLU<T>(name: "DNI3 ReLU1"),
                new Linear<T>(1024, 1024, name: "DNI3 Linear2"),
                new BatchNormalization<T>(1024, name: "DNI3 Nrom2"),
                new ReLU<T>(name: "DNI3 ReLU2"),
                new Linear<T>(1024, 256, initialW: new Real<T>[1024, 256], name: "DNI3 Linear3")
            );

            //optimizerを宣言
            Layer1.SetOptimizer(new Adam<T>());
            Layer2.SetOptimizer(new Adam<T>());
            Layer3.SetOptimizer(new Adam<T>());
            Layer4.SetOptimizer(new Adam<T>());

            DNI1.SetOptimizer(new Adam<T>());
            DNI2.SetOptimizer(new Adam<T>());
            DNI3.SetOptimizer(new Adam<T>());

            //三世代学習
            for (int epoch = 0; epoch < 20; epoch++)
            {
                Console.WriteLine("epoch " + (epoch + 1));

                Real<T> totalLoss = 0;
                Real<T> DNI1totalLoss = 0;
                Real<T> DNI2totalLoss = 0;
                Real<T> DNI3totalLoss = 0;

                long totalLossCount = 0;
                long DNI1totalLossCount = 0;
                long DNI2totalLossCount = 0;
                long DNI3totalLossCount = 0;

                //何回バッチを実行するか
                for (int i = 1; i < TRAIN_DATA_COUNT + 1; i++)
                {
                    //訓練データからランダムにデータを取得
                    TestDataSet<T> datasetX = mnistData.GetRandomXSet(BATCH_DATA_COUNT);

                    //第一層を実行
                    NdArray<T>[] layer1ForwardResult = Layer1.Forward(datasetX.Data);

                    //第一層の傾きを取得
                    NdArray<T>[] DNI1Result = DNI1.Forward(layer1ForwardResult);

                    //第一層の傾きを適用
                    layer1ForwardResult[0].Grad = DNI1Result[0].Data.ToArray();

                    //第一層を更新
                    Layer1.Backward(layer1ForwardResult);
                    layer1ForwardResult[0].ParentFunc = null; //Backwardを実行したので計算グラフを切っておく
                    Layer1.Update();

                    //第二層を実行
                    NdArray<T>[] layer2ForwardResult = Layer2.Forward(layer1ForwardResult);

                    //第二層の傾きを取得
                    NdArray<T>[] DNI2Result = DNI2.Forward(layer2ForwardResult);

                    //第二層の傾きを適用
                    layer2ForwardResult[0].Grad = DNI2Result[0].Data.ToArray();

                    //第二層を更新
                    Layer2.Backward(layer2ForwardResult);
                    layer2ForwardResult[0].ParentFunc = null;

                    //第一層用のDNIの学習を実行
                    Real<T> DNI1loss = new MeanSquaredError<T>().Evaluate(DNI1Result, new NdArray<T>(layer1ForwardResult[0].Grad, DNI1Result[0].Shape, DNI1Result[0].BatchCount));

                    Layer2.Update();

                    DNI1.Backward(DNI1Result);
                    DNI1.Update();

                    DNI1totalLoss += DNI1loss;
                    DNI1totalLossCount++;

                    //第三層を実行
                    NdArray<T>[] layer3ForwardResult = Layer3.Forward(layer2ForwardResult);

                    //第三層の傾きを取得
                    NdArray<T>[] DNI3Result = DNI3.Forward(layer3ForwardResult);

                    //第三層の傾きを適用
                    layer3ForwardResult[0].Grad = DNI3Result[0].Data.ToArray();

                    //第三層を更新
                    Layer3.Backward(layer3ForwardResult);
                    layer3ForwardResult[0].ParentFunc = null;

                    //第二層用のDNIの学習を実行
                    Real<T> DNI2loss = new MeanSquaredError<T>().Evaluate(DNI2Result, new NdArray<T>(layer2ForwardResult[0].Grad, DNI2Result[0].Shape, DNI2Result[0].BatchCount));

                    Layer3.Update();

                    DNI2.Backward(DNI2Result);
                    DNI2.Update();

                    DNI2totalLoss += DNI2loss;
                    DNI2totalLossCount++;

                    //第四層を実行
                    NdArray<T>[] layer4ForwardResult = Layer4.Forward(layer3ForwardResult);

                    //第四層の傾きを取得
                    Real<T> sumLoss = new SoftmaxCrossEntropy<T>().Evaluate(layer4ForwardResult, datasetX.Label);

                    //第四層を更新
                    Layer4.Backward(layer4ForwardResult);
                    layer4ForwardResult[0].ParentFunc = null;

                    totalLoss += sumLoss;
                    totalLossCount++;

                    //第三層用のDNIの学習を実行
                    Real<T> DNI3loss = new MeanSquaredError<T>().Evaluate(DNI3Result, new NdArray<T>(layer3ForwardResult[0].Grad, DNI3Result[0].Shape, DNI3Result[0].BatchCount));

                    Layer4.Update();

                    DNI3.Backward(DNI3Result);
                    DNI3.Update();

                    DNI3totalLoss += DNI3loss;
                    DNI3totalLossCount++;

                    Console.WriteLine("\nbatch count " + i + "/" + TRAIN_DATA_COUNT);
                    //結果出力
                    Console.WriteLine("total loss " + totalLoss / totalLossCount);
                    Console.WriteLine("local loss " + sumLoss);

                    Console.WriteLine("\nDNI1 total loss " + DNI1totalLoss / DNI1totalLossCount);
                    Console.WriteLine("DNI2 total loss " + DNI2totalLoss / DNI2totalLossCount);
                    Console.WriteLine("DNI3 total loss " + DNI3totalLoss / DNI3totalLossCount);

                    Console.WriteLine("\nDNI1 local loss " + DNI1loss);
                    Console.WriteLine("DNI2 local loss " + DNI2loss);
                    Console.WriteLine("DNI3 local loss " + DNI3loss);

                    //20回バッチを動かしたら精度をテストする
                    if (i % 20 == 0)
                    {
                        Console.WriteLine("\nTesting...");

                        //テストデータからランダムにデータを取得
                        TestDataSet<T> datasetY = mnistData.GetRandomYSet(TEST_DATA_COUNT);

                        //テストを実行
                        Real<T> accuracy = Trainer<T>.Accuracy(nn, datasetY.Data, datasetY.Label);
                        Console.WriteLine("accuracy " + accuracy);
                    }
                }
            }
        }
    }
}
