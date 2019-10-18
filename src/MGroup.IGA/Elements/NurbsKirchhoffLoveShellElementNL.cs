using System.Diagnostics.Contracts;
using MGroup.MSolve.Discretization.Commons;

namespace MGroup.IGA.Elements
{
	using System;
	using System.Collections.Generic;
	using System.Linq;

	using MGroup.IGA.Entities;
	using MGroup.IGA.Entities.Loads;
	using MGroup.IGA.Interfaces;
	using MGroup.IGA.SupportiveClasses;
	using MGroup.LinearAlgebra.Matrices;
	using MGroup.LinearAlgebra.Vectors;
	using MGroup.Materials.Interfaces;
	using MGroup.MSolve.Discretization;
	using MGroup.MSolve.Discretization.FreedomDegrees;
	using MGroup.MSolve.Discretization.Interfaces;
	using MGroup.MSolve.Discretization.Loads;
	using MGroup.MSolve.Discretization.Mesh;

	public class NurbsKirchhoffLoveShellElementNL : Element, IStructuralIsogeometricElement, ISurfaceLoadedElement
	{
		protected static readonly IDofType[] ControlPointDofTypes = { StructuralDof.TranslationX, StructuralDof.TranslationY, StructuralDof.TranslationZ };
		private IDofType[][] dofTypes;

		private Dictionary<GaussLegendrePoint3D, List<GaussLegendrePoint3D>> thicknessIntegrationPoints =
			new Dictionary<GaussLegendrePoint3D, List<GaussLegendrePoint3D>>();

		private Dictionary<GaussLegendrePoint3D, Dictionary<GaussLegendrePoint3D, IShellMaterial>>
			materialsAtThicknessGP = new Dictionary<GaussLegendrePoint3D, Dictionary<GaussLegendrePoint3D, IShellMaterial>>();

		private bool isInitialized;
		private double[] _solution;

		public NurbsKirchhoffLoveShellElementNL(IShellMaterial shellMaterial, IList<Knot> elementKnots, IList<ControlPoint> elementControlPoints, Patch patch, double thickness)
		{
			Contract.Requires(shellMaterial != null);
			this.Patch = patch;
			this.Thickness = thickness;
			foreach (var knot in elementKnots)
			{
				if (!KnotsDictionary.ContainsKey(knot.ID))
					this.KnotsDictionary.Add(knot.ID, knot);
			}

			_solution = new double[3 * elementControlPoints.Count];

			foreach (var controlPoint in elementControlPoints)
			{
				if (!ControlPointsDictionary.ContainsKey(controlPoint.ID))
					ControlPointsDictionary.Add(controlPoint.ID, controlPoint);
			}
			CreateElementGaussPoints(this);
			foreach (var medianSurfaceGP in thicknessIntegrationPoints.Keys)
			{
				materialsAtThicknessGP.Add(medianSurfaceGP, new Dictionary<GaussLegendrePoint3D, IShellMaterial>());
				foreach (var point in thicknessIntegrationPoints[medianSurfaceGP])
				{
					materialsAtThicknessGP[medianSurfaceGP].Add(point, shellMaterial.Clone());
				}
			}
		}

		public CellType CellType { get; } = CellType.Unknown;

		public IElementDofEnumerator DofEnumerator { get; set; } = new GenericDofEnumerator();

		public ElementDimensions ElementDimensions => ElementDimensions.ThreeD;

		public bool MaterialModified => false;

		public double[] CalculateAccelerationForces(IElement element, IList<MassAccelerationLoad> loads) => throw new NotImplementedException();

		public double[,] CalculateDisplacementsForPostProcessing(Element element, Matrix localDisplacements)
		{
			var nurbsElement = (NurbsKirchhoffLoveShellElementNL)element;
			var knotParametricCoordinatesKsi = Vector.CreateFromArray(Knots.Select(k => k.Ksi).ToArray());
			var knotParametricCoordinatesHeta = Vector.CreateFromArray(Knots.Select(k => k.Heta).ToArray());

			var nurbs = new Nurbs2D(nurbsElement, nurbsElement.ControlPoints.ToArray(), knotParametricCoordinatesKsi, knotParametricCoordinatesHeta);

			var knotDisplacements = new double[4, 3];
			var paraviewKnotRenumbering = new int[] { 0, 3, 1, 2 };
			for (var j = 0; j < knotDisplacements.GetLength(0); j++)
			{
				for (int i = 0; i < element.ControlPoints.Count(); i++)
				{
					knotDisplacements[paraviewKnotRenumbering[j], 0] +=
						nurbs.NurbsValues[i, j] * localDisplacements[i, 0];
					knotDisplacements[paraviewKnotRenumbering[j], 1] +=
						nurbs.NurbsValues[i, j] * localDisplacements[i, 1];
					knotDisplacements[paraviewKnotRenumbering[j], 2] +=
						nurbs.NurbsValues[i, j] * localDisplacements[i, 2];
				}
			}

			return knotDisplacements;
		}

		public double[] CalculateForces(IElement element, double[] localDisplacements, double[] localdDisplacements)
		{
			var shellElement = (NurbsKirchhoffLoveShellElementNL)element;
			var controlPoints = shellElement.ControlPoints.ToArray();
			var elementNodalForces = new double[shellElement.ControlPointsDictionary.Count * 3];

			_solution = localDisplacements;
			var newControlPoints = CurrentControlPoint(controlPoints);

			var nurbs = new Nurbs2D(shellElement, shellElement.ControlPoints.ToArray());
			var gaussPoints = materialsAtThicknessGP.Keys.ToArray();

			for (int j = 0; j < this.materialsAtThicknessGP.Count; j++)
			{
				var jacobianMatrix = CalculateJacobian(newControlPoints, nurbs, j);

				var hessianMatrix = CalculateHessian(newControlPoints, nurbs, j);

				var surfaceBasisVector1 = CalculateSurfaceBasisVector1(jacobianMatrix, 0);

				var surfaceBasisVector2 = CalculateSurfaceBasisVector1(jacobianMatrix, 1);

				var surfaceBasisVector3 = new[]
				{
					surfaceBasisVector1[1] * surfaceBasisVector2[2] - surfaceBasisVector1[2] * surfaceBasisVector2[1],
					surfaceBasisVector1[2] * surfaceBasisVector2[0] - surfaceBasisVector1[0] * surfaceBasisVector2[2],
					surfaceBasisVector1[0] * surfaceBasisVector2[1] - surfaceBasisVector1[1] * surfaceBasisVector2[0],
				};

				double norm = surfaceBasisVector3.Sum(t => t * t);

				var J1 = Math.Sqrt(norm);

				for (int i = 0; i < surfaceBasisVector3.Length; i++)
					surfaceBasisVector3[i] = surfaceBasisVector3[i] / J1;

				var surfaceBasisVectorDerivative1 = CalculateSurfaceBasisVector1(hessianMatrix, 0);
				var surfaceBasisVectorDerivative2 = CalculateSurfaceBasisVector1(hessianMatrix, 1);
				var surfaceBasisVectorDerivative12 = CalculateSurfaceBasisVector1(hessianMatrix, 2);

				var Bmembrane = CalculateMembraneDeformationMatrix(controlPoints, nurbs, j, surfaceBasisVector1,
					surfaceBasisVector2);
				var Bbending = CalculateBendingDeformationMatrix(controlPoints, surfaceBasisVector3, nurbs, j, surfaceBasisVector2,
					surfaceBasisVectorDerivative1, surfaceBasisVector1, J1, surfaceBasisVectorDerivative2,
					surfaceBasisVectorDerivative12);

				var (membraneForces, BendingMoments) =
					IntegratedStressesOverThickness(gaussPoints, j);

				var wfactor = InitialJ1[j] * gaussPoints[j].WeightFactor;
				for (int i = 0; i < Bmembrane.GetLength(1); i++)
				{
					for (int k = 0; k < Bmembrane.GetLength(0); k++)
					{
						elementNodalForces[i] += Bmembrane[k, i] * membraneForces[k] * wfactor +
												 Bbending[k, i] * BendingMoments[k] * wfactor;
					}
				}
			}

			return elementNodalForces;
		}

		public double[] CalculateForcesForLogging(IElement element, double[] localDisplacements)
		{
			throw new NotImplementedException();
		}

		public Dictionary<int, double> CalculateLoadingCondition(Element element, Edge edge, NeumannBoundaryCondition neumann) => throw new NotImplementedException();

		public Dictionary<int, double> CalculateLoadingCondition(Element element, Face face, NeumannBoundaryCondition neumann) => throw new NotImplementedException();

		public Dictionary<int, double> CalculateLoadingCondition(Element element, Edge edge, PressureBoundaryCondition pressure) => throw new NotImplementedException();

		public Dictionary<int, double> CalculateLoadingCondition(Element element, Face face, PressureBoundaryCondition pressure) => throw new NotImplementedException();

		public Tuple<double[], double[]> CalculateStresses(IElement element, double[] localDisplacements, double[] localdDisplacements)
		{
			var shellElement = (NurbsKirchhoffLoveShellElementNL)element;
			var elementControlPoints = shellElement.ControlPoints.ToArray();
			var nurbs = new Nurbs2D(shellElement, elementControlPoints);

			_solution = localDisplacements;

			var newControlPoints = CurrentControlPoint(elementControlPoints);

			for (var j = 0; j < materialsAtThicknessGP.Keys.Count; j++)
			{
				var jacobianMatrix = CalculateJacobian(newControlPoints, nurbs, j);

				var hessianMatrix = CalculateHessian(newControlPoints, nurbs, j);

				var surfaceBasisVector1 = CalculateSurfaceBasisVector1(jacobianMatrix, 0);

				var surfaceBasisVector2 = CalculateSurfaceBasisVector1(jacobianMatrix, 1);

				var surfaceBasisVector3 = new[]
				{
					surfaceBasisVector1[1] * surfaceBasisVector2[2] - surfaceBasisVector1[2] * surfaceBasisVector2[1],
					surfaceBasisVector1[2] * surfaceBasisVector2[0] - surfaceBasisVector1[0] * surfaceBasisVector2[2],
					surfaceBasisVector1[0] * surfaceBasisVector2[1] - surfaceBasisVector1[1] * surfaceBasisVector2[0]
				};

				var norm = surfaceBasisVector3.Sum(t => t * t);
				var J1 = Math.Sqrt(norm);

				var surfaceBasisVectorDerivative1 = CalculateSurfaceBasisVector1(hessianMatrix, 0);
				var surfaceBasisVectorDerivative2 = CalculateSurfaceBasisVector1(hessianMatrix, 1);
				var surfaceBasisVectorDerivative12 = CalculateSurfaceBasisVector1(hessianMatrix, 2);

				var A11 = initialSurfaceBasisVectors1[j][0] * initialSurfaceBasisVectors1[j][0] +
						  initialSurfaceBasisVectors1[j][1] * initialSurfaceBasisVectors1[j][1] +
						  initialSurfaceBasisVectors1[j][2] * initialSurfaceBasisVectors1[j][2];

				var A22 = initialSurfaceBasisVectors2[j][0] * initialSurfaceBasisVectors2[j][0] +
						 initialSurfaceBasisVectors2[j][1] * initialSurfaceBasisVectors2[j][1] +
						 initialSurfaceBasisVectors2[j][2] * initialSurfaceBasisVectors2[j][2];

				var A12 = initialSurfaceBasisVectors1[j][0] * initialSurfaceBasisVectors2[j][0] +
						  initialSurfaceBasisVectors1[j][1] * initialSurfaceBasisVectors2[j][1] +
						  initialSurfaceBasisVectors1[j][2] * initialSurfaceBasisVectors2[j][2];

				var a11 = surfaceBasisVector1[0] * surfaceBasisVector1[0] +
						  surfaceBasisVector1[1] * surfaceBasisVector1[1] +
						  surfaceBasisVector1[2] * surfaceBasisVector1[2];

				var a22 = surfaceBasisVector2[0] * surfaceBasisVector2[0] +
						  surfaceBasisVector2[1] * surfaceBasisVector2[1] +
						  surfaceBasisVector2[2] * surfaceBasisVector2[2];

				var a12 = surfaceBasisVector1[0] * surfaceBasisVector2[0] +
						  surfaceBasisVector1[1] * surfaceBasisVector2[1] +
						  surfaceBasisVector1[2] * surfaceBasisVector2[2];

				var membraneStrain = new double[] { 0.5 * (a11 - A11), 0.5 * (a22 - A22), a12 - A12 };

				var B11 = initialSurfaceBasisVectorDerivative1[j][0] * initialSurfaceBasisVectors3[j][0] +
						  initialSurfaceBasisVectorDerivative1[j][1] * initialSurfaceBasisVectors3[j][1] +
						  initialSurfaceBasisVectorDerivative1[j][2] * initialSurfaceBasisVectors3[j][2];

				var B22 = initialSurfaceBasisVectorDerivative2[j][0] * initialSurfaceBasisVectors3[j][0] +
						  initialSurfaceBasisVectorDerivative2[j][1] * initialSurfaceBasisVectors3[j][1] +
						  initialSurfaceBasisVectorDerivative2[j][2] * initialSurfaceBasisVectors3[j][2];

				var B12 = initialSurfaceBasisVectorDerivative12[j][0] * initialSurfaceBasisVectors3[j][0] +
						  initialSurfaceBasisVectorDerivative12[j][1] * initialSurfaceBasisVectors3[j][1] +
						  initialSurfaceBasisVectorDerivative12[j][2] * initialSurfaceBasisVectors3[j][2];

				var b11 = surfaceBasisVectorDerivative1[0] * surfaceBasisVector3[0] +
						  surfaceBasisVectorDerivative1[1] * surfaceBasisVector3[1] +
						  surfaceBasisVectorDerivative1[2] * surfaceBasisVector3[2];

				var b22 = surfaceBasisVectorDerivative2[0] * surfaceBasisVector3[0] +
						  surfaceBasisVectorDerivative2[1] * surfaceBasisVector3[1] +
						  surfaceBasisVectorDerivative2[2] * surfaceBasisVector3[2];

				var b12 = surfaceBasisVectorDerivative12[0] * surfaceBasisVector3[0] +
						 surfaceBasisVectorDerivative12[1] * surfaceBasisVector3[1] +
						 surfaceBasisVectorDerivative12[2] * surfaceBasisVector3[2];

				var bendingStrain = new double[] { b11 - B11, b22 - B22, b12 - B12 };

				foreach (var keyValuePair in materialsAtThicknessGP[materialsAtThicknessGP.Keys.ToList()[j]])
				{
					var thicknessPoint = keyValuePair.Key;
					var material = keyValuePair.Value;
					var gpStrain = new double[bendingStrain.Length];
					var z = -thicknessPoint.Zeta;
					for (var i = 0; i < bendingStrain.Length; i++)
					{
						gpStrain[i] += membraneStrain[i] + bendingStrain[i] * z;
					}

					material.UpdateMaterial(gpStrain);
				}
			}

			return new Tuple<double[], double[]>(new double[0], new double[0]);
		}

		public Dictionary<int, double> CalculateSurfaceDistributedLoad(Element element, IDofType loadedDof, double loadMagnitude)
		{
			var shellElement = (NurbsKirchhoffLoveShellElementNL)element;
			var elementControlPoints = shellElement.ControlPoints.ToArray();
			var gaussPoints = CreateElementGaussPoints(shellElement);
			var distributedLoad = new Dictionary<int, double>();
			var nurbs = new Nurbs2D(shellElement, elementControlPoints);

			for (var j = 0; j < gaussPoints.Count; j++)
			{
				var jacobianMatrix = CalculateJacobian(elementControlPoints, nurbs, j);
				var surfaceBasisVector1 = CalculateSurfaceBasisVector1(jacobianMatrix, 0);
				var surfaceBasisVector2 = CalculateSurfaceBasisVector1(jacobianMatrix, 1);
				var surfaceBasisVector3 = surfaceBasisVector1.CrossProduct(surfaceBasisVector2);
				var J1 = surfaceBasisVector3.Norm2();
				surfaceBasisVector3.ScaleIntoThis(1 / J1);

				for (int i = 0; i < elementControlPoints.Length; i++)
				{
					var loadedDofIndex = ControlPointDofTypes.FindFirstIndex(loadedDof);
					if (!element.Model.GlobalDofOrdering.GlobalFreeDofs.Contains(elementControlPoints[i], loadedDof))
						continue;
					var dofId = element.Model.GlobalDofOrdering.GlobalFreeDofs[elementControlPoints[i], loadedDof];

					if (distributedLoad.ContainsKey(dofId))
					{
						distributedLoad[dofId] += loadMagnitude * J1 *
												  nurbs.NurbsValues[i, j] * gaussPoints[j].WeightFactor;
					}
					else
					{
						distributedLoad.Add(dofId, loadMagnitude * nurbs.NurbsValues[i, j] * J1 * gaussPoints[j].WeightFactor);
					}
				}
			}

			return distributedLoad;
		}

		public Dictionary<int, double> CalculateSurfacePressure(Element element, double pressureMagnitude)
		{
			var shellElement = (NurbsKirchhoffLoveShellElementNL)element;
			var elementControlPoints = shellElement.ControlPoints.ToArray();
			var gaussPoints = CreateElementGaussPoints(shellElement);
			var pressureLoad = new Dictionary<int, double>();
			var nurbs = new Nurbs2D(shellElement, elementControlPoints);

			for (var j = 0; j < gaussPoints.Count; j++)
			{
				var jacobianMatrix = CalculateJacobian(elementControlPoints, nurbs, j);
				var surfaceBasisVector1 = CalculateSurfaceBasisVector1(jacobianMatrix, 0);
				var surfaceBasisVector2 = CalculateSurfaceBasisVector1(jacobianMatrix, 1);
				var surfaceBasisVector3 = surfaceBasisVector1.CrossProduct(surfaceBasisVector2);
				var J1 = surfaceBasisVector3.Norm2();
				surfaceBasisVector3.ScaleIntoThis(1 / J1);

				for (int i = 0; i < elementControlPoints.Length; i++)
				{
					for (int k = 0; k < ControlPointDofTypes.Length; k++)
					{
						int dofId = element.Model.GlobalDofOrdering.GlobalFreeDofs[elementControlPoints[i], ControlPointDofTypes[k]];

						if (pressureLoad.ContainsKey(dofId))
						{
							pressureLoad[dofId] += pressureMagnitude * surfaceBasisVector3[k] *
												   nurbs.NurbsValues[i, j] * gaussPoints[j].WeightFactor;
						}
						else
						{
							pressureLoad.Add(dofId, pressureMagnitude * surfaceBasisVector3[k] * nurbs.NurbsValues[i, j] * gaussPoints[j].WeightFactor);
						}
					}
				}
			}

			return pressureLoad;
		}

		public void ClearMaterialState()
		{
		}

		public void ClearMaterialStresses() => throw new NotImplementedException();

		public IMatrix DampingMatrix(IElement element) => throw new NotImplementedException();

		public IReadOnlyList<IReadOnlyList<IDofType>> GetElementDofTypes(IElement element)
		{
			dofTypes = new IDofType[element.Nodes.Count][];
			for (var i = 0; i < element.Nodes.Count; i++)
			{
				dofTypes[i] = ControlPointDofTypes;
			}

			return dofTypes;
		}

		public (double[,] MembraneConstitutiveMatrix, double[,] BendingConstitutiveMatrix, double[,]
			CouplingConstitutiveMatrix) IntegratedConstitutiveOverThickness(IList<GaussLegendrePoint3D> gaussPoints,
				int j)
		{
			var MembraneConstitutiveMatrix = new double[3, 3];
			var BendingConstitutiveMatrix = new double[3, 3];
			var CouplingConstitutiveMatrix = new double[3, 3];

			foreach (var keyValuePair in materialsAtThicknessGP[gaussPoints[j]])
			{
				var thicknessPoint = keyValuePair.Key;
				var material = keyValuePair.Value;
				var constitutiveMatrixM = material.ConstitutiveMatrix;
				double tempc = 0;
				double w = thicknessPoint.WeightFactor;
				double z = thicknessPoint.Zeta;
				for (int i = 0; i < 3; i++)
				{
					for (int k = 0; k < 3; k++)
					{
						tempc = constitutiveMatrixM[i, k];
						MembraneConstitutiveMatrix[i, k] += tempc * w;
						CouplingConstitutiveMatrix[i, k] += tempc * w * z;
						BendingConstitutiveMatrix[i, k] += tempc * w * z * z;
					}
				}
			}

			return (MembraneConstitutiveMatrix, BendingConstitutiveMatrix, CouplingConstitutiveMatrix);
		}

		public (double[] MembraneForces, double[] BendingMoments) IntegratedStressesOverThickness(
			IList<GaussLegendrePoint3D> gaussPoints, int j)
		{
			var MembraneForces = new double[3];
			var BendingMoments = new double[3];

			foreach (var keyValuePair in materialsAtThicknessGP[gaussPoints[j]])
			{
				var thicknessPoint = keyValuePair.Key;
				var material = keyValuePair.Value;
				var w = thicknessPoint.WeightFactor;
				var z = thicknessPoint.Zeta;
				for (int i = 0; i < 3; i++)
				{
					MembraneForces[i] += material.Stresses[i] * w * Thickness / 2;
					BendingMoments[i] += material.Stresses[i] * w * z * z * Thickness / 2;
				}
			}

			return (MembraneForces, BendingMoments);
		}

		public IMatrix MassMatrix(IElement element) => throw new NotImplementedException();

		public void ResetMaterialModified() => throw new NotImplementedException();

		public void SaveMaterialState()
		{
			foreach (var gp in materialsAtThicknessGP.Keys)
			{
				foreach (var material in materialsAtThicknessGP[gp].Values)
				{
					material.SaveState();
				}
			}
		}

		public IMatrix StiffnessMatrix(IElement element)
		{
			var shellElement = (NurbsKirchhoffLoveShellElementNL)element;
			var gaussPoints = materialsAtThicknessGP.Keys.ToArray();

			var controlPoints = shellElement.ControlPoints.ToArray();
			var nurbs = new Nurbs2D(shellElement, controlPoints);

			if (!isInitialized)
			{
				CalculateInitialConfigurationData(controlPoints, nurbs, gaussPoints);
				isInitialized = true;
			}

			var elementControlPoints = CurrentControlPoint(controlPoints);

			var bRows = 3;
			var bCols = elementControlPoints.Length * 3;
			var stiffnessMatrix = new double[bCols, bCols];
			var BmTranspose = new double[bCols, bRows];
			var BbTranspose = new double[bCols, bRows];

			var BmTransposeMultStiffness = new double[bCols, bRows];
			var BbTransposeMultStiffness = new double[bCols, bRows];
			var BmbTransposeMultStiffness = new double[bCols, bRows];
			var BbmTransposeMultStiffness = new double[bCols, bRows];

			for (int j = 0; j < gaussPoints.Length; j++)
			{
				var jacobianMatrix = CalculateJacobian(elementControlPoints, nurbs, j);

				var hessianMatrix = CalculateHessian(elementControlPoints, nurbs, j);
				var surfaceBasisVector1 = CalculateSurfaceBasisVector1(jacobianMatrix, 0);

				var surfaceBasisVector2 = CalculateSurfaceBasisVector1(jacobianMatrix, 1);

				var surfaceBasisVector3 = new[]
				{
					surfaceBasisVector1[1] * surfaceBasisVector2[2] - surfaceBasisVector1[2] * surfaceBasisVector2[1],
					surfaceBasisVector1[2] * surfaceBasisVector2[0] - surfaceBasisVector1[0] * surfaceBasisVector2[2],
					surfaceBasisVector1[0] * surfaceBasisVector2[1] - surfaceBasisVector1[1] * surfaceBasisVector2[0],
				};

				double norm = 0;
				for (int i = 0; i < surfaceBasisVector3.Length; i++)
					norm += surfaceBasisVector3[i] * surfaceBasisVector3[i];
				var J1 = Math.Sqrt(norm);

				for (int i = 0; i < surfaceBasisVector3.Length; i++)
					surfaceBasisVector3[i] = surfaceBasisVector3[i] / J1;

				var surfaceBasisVectorDerivative1 = CalculateSurfaceBasisVector1(hessianMatrix, 0);
				var surfaceBasisVectorDerivative2 = CalculateSurfaceBasisVector1(hessianMatrix, 1);
				var surfaceBasisVectorDerivative12 = CalculateSurfaceBasisVector1(hessianMatrix, 2);

				var Bmembrane = CalculateMembraneDeformationMatrix(elementControlPoints, nurbs, j, surfaceBasisVector1,
					surfaceBasisVector2);
				var Bbending = CalculateBendingDeformationMatrix(elementControlPoints, surfaceBasisVector3, nurbs, j, surfaceBasisVector2,
					surfaceBasisVectorDerivative1, surfaceBasisVector1, J1, surfaceBasisVectorDerivative2,
					surfaceBasisVectorDerivative12);

				var (MembraneConstitutiveMatrix, BendingConstitutiveMatrix, CouplingConstitutiveMatrix) =
					IntegratedConstitutiveOverThickness(gaussPoints, j);

				double wFactor = InitialJ1[j] * gaussPoints[j].WeightFactor;
				double tempb = 0;
				double tempm = 0;
				Array.Clear(BmTranspose, 0, bRows * bCols);
				Array.Clear(BbTranspose, 0, bRows * bCols);
				for (int i = 0; i < bRows; i++)
				{
					for (int k = 0; k < bCols; k++)
					{
						BmTranspose[k, i] = Bmembrane[i, k] * wFactor;
						BbTranspose[k, i] = Bbending[i, k] * wFactor;
					}
				}

				double tempcm = 0;
				double tempcb = 0;
				double tempcc = 0;
				Array.Clear(BmTransposeMultStiffness, 0, bRows * bCols);
				Array.Clear(BbTransposeMultStiffness, 0, bRows * bCols);
				Array.Clear(BmbTransposeMultStiffness, 0, bRows * bCols);
				Array.Clear(BbmTransposeMultStiffness, 0, bRows * bCols);
				for (int i = 0; i < bCols; i++)
				{
					for (int k = 0; k < bRows; k++)
					{
						tempm = BmTranspose[i, k];
						tempb = BbTranspose[i, k];
						for (int m = 0; m < bRows; m++)
						{
							tempcm = MembraneConstitutiveMatrix[k, m];
							tempcb = BendingConstitutiveMatrix[k, m];
							tempcc = CouplingConstitutiveMatrix[k, m];

							BmTransposeMultStiffness[i, m] += tempm * tempcm;
							BbTransposeMultStiffness[i, m] += tempb * tempcb;
							BmbTransposeMultStiffness[i, m] += tempm * tempcc;
							BbmTransposeMultStiffness[i, m] += tempb * tempcc;
						}
					}
				}

				double tempmb = 0;
				double tempbm = 0;
				double mem = 0;
				double ben = 0;
				for (int i = 0; i < bCols; i++)
				{
					for (int k = 0; k < bRows; k++)
					{
						tempm = BmTransposeMultStiffness[i, k];
						tempb = BbTransposeMultStiffness[i, k];
						tempmb = BmbTransposeMultStiffness[i, k];
						tempbm = BbmTransposeMultStiffness[i, k];

						for (int m = 0; m < bCols; m++)
						{
							mem = Bmembrane[k, m];
							ben = Bbending[k, m];
							stiffnessMatrix[i, m] += tempm * mem + tempb * ben + tempmb * ben + tempbm * mem;
						}
					}
				}

				var (MembraneForces, BendingMoments) = IntegratedStressesOverThickness(gaussPoints, j);

				var KmembraneNL = CalculateKmembraneNL(elementControlPoints, MembraneForces, nurbs, j);

				var KbendingNL = CalculateKbendingNL(elementControlPoints, BendingMoments, nurbs,
					surfaceBasisVector1, surfaceBasisVector2,
					surfaceBasisVector3,
					surfaceBasisVectorDerivative1,
					surfaceBasisVectorDerivative2,
					surfaceBasisVectorDerivative12, J1, j);

				for (var i = 0; i < stiffnessMatrix.GetLength(0); i++)
				{
					for (var k = 0; k < stiffnessMatrix.GetLength(1); k++)
					{
						stiffnessMatrix[i, k] += KmembraneNL[i, k]* wFactor;
						stiffnessMatrix[i, k] += KbendingNL[i, k]* wFactor;
					}
				}
			}

			return Matrix.CreateFromArray(stiffnessMatrix);
		}

		private ControlPoint[] CurrentControlPoint(ControlPoint[] controlPoints)
		{
			var cp = new ControlPoint[controlPoints.Length];

			for (int i = 0; i < controlPoints.Length; i++)
			{
				cp[i] = new ControlPoint()
				{
					X = controlPoints[i].X + _solution[i * 3],
					Y = controlPoints[i].Y + _solution[i * 3 + 1],
					Z = controlPoints[i].Z + _solution[i * 3 + 2],
					Ksi = controlPoints[i].Ksi,
					Heta = controlPoints[i].Heta,
					Zeta = controlPoints[i].Zeta,
					WeightFactor = controlPoints[i].WeightFactor
				};
			}

			return cp;
		}

		private static double[,] CalculateHessian(ControlPoint[] controlPoints, Nurbs2D nurbs, int j)
		{
			var hessianMatrix = new double[3, 3];
			for (var k = 0; k < controlPoints.Length; k++)
			{
				hessianMatrix[0, 0] +=
					nurbs.NurbsSecondDerivativeValueKsi[k, j] * controlPoints[k].X;
				hessianMatrix[0, 1] +=
					nurbs.NurbsSecondDerivativeValueKsi[k, j] * controlPoints[k].Y;
				hessianMatrix[0, 2] +=
					nurbs.NurbsSecondDerivativeValueKsi[k, j] * controlPoints[k].Z;
				hessianMatrix[1, 0] +=
					nurbs.NurbsSecondDerivativeValueHeta[k, j] * controlPoints[k].X;
				hessianMatrix[1, 1] +=
					nurbs.NurbsSecondDerivativeValueHeta[k, j] * controlPoints[k].Y;
				hessianMatrix[1, 2] +=
					nurbs.NurbsSecondDerivativeValueHeta[k, j] * controlPoints[k].Z;
				hessianMatrix[2, 0] +=
					nurbs.NurbsSecondDerivativeValueKsiHeta[k, j] * controlPoints[k].X;
				hessianMatrix[2, 1] +=
					nurbs.NurbsSecondDerivativeValueKsiHeta[k, j] * controlPoints[k].Y;
				hessianMatrix[2, 2] +=
					nurbs.NurbsSecondDerivativeValueKsiHeta[k, j] * controlPoints[k].Z;
			}

			return hessianMatrix;
		}

		private static double[,] CalculateJacobian(ControlPoint[] controlPoints, Nurbs2D nurbs, int j)
		{
			var jacobianMatrix = new double[2, 3];
			for (var k = 0; k < controlPoints.Length; k++)
			{
				jacobianMatrix[0, 0] += nurbs.NurbsDerivativeValuesKsi[k, j] * controlPoints[k].X;
				jacobianMatrix[0, 1] += nurbs.NurbsDerivativeValuesKsi[k, j] * controlPoints[k].Y;
				jacobianMatrix[0, 2] += nurbs.NurbsDerivativeValuesKsi[k, j] * controlPoints[k].Z;
				jacobianMatrix[1, 0] += nurbs.NurbsDerivativeValuesHeta[k, j] * controlPoints[k].X;
				jacobianMatrix[1, 1] += nurbs.NurbsDerivativeValuesHeta[k, j] * controlPoints[k].Y;
				jacobianMatrix[1, 2] += nurbs.NurbsDerivativeValuesHeta[k, j] * controlPoints[k].Z;
			}

			return jacobianMatrix;
		}

		private static double[] CalculateSurfaceBasisVector1(double[,] Matrix, int row)
		{
			var surfaceBasisVector1 = new double[3];
			surfaceBasisVector1[0] = Matrix[row, 0];
			surfaceBasisVector1[1] = Matrix[row, 1];
			surfaceBasisVector1[2] = Matrix[row, 2];
			return surfaceBasisVector1;
		}

		private double[,] CalculateA3r(double dKsi, double dHeta,
			double[] surfaceBasisVector2, double[] surfaceBasisVector1)
		{
			var a3r = new double[3, 3];
			a3r[0, 1] = -dKsi * surfaceBasisVector2[2] + surfaceBasisVector1[2] * dHeta;
			a3r[0, 2] = dKsi * surfaceBasisVector2[1] + -surfaceBasisVector1[1] * dHeta;

			a3r[1, 0] = dKsi * surfaceBasisVector2[2] - surfaceBasisVector1[2] * dHeta;
			a3r[1, 2] = -dKsi * surfaceBasisVector2[0] + surfaceBasisVector1[0] * dHeta;

			a3r[2, 0] = -dKsi * surfaceBasisVector2[1] + surfaceBasisVector1[1] * dHeta;
			a3r[2, 1] = dKsi * surfaceBasisVector2[0] + -surfaceBasisVector1[0] * dHeta;
			return a3r;
		}

		private double[,] CalculateBendingDeformationMatrix(ControlPoint[] controlPoints, double[] surfaceBasisVector3,
			Nurbs2D nurbs, int j, double[] surfaceBasisVector2, double[] surfaceBasisVectorDerivative1, double[] surfaceBasisVector1,
			double J1, double[] surfaceBasisVectorDerivative2, double[] surfaceBasisVectorDerivative12)
		{
			var Bbending = new double[3, controlPoints.Length * 3];
			var s1 = Vector.CreateFromArray(surfaceBasisVector1);
			var s2 = Vector.CreateFromArray(surfaceBasisVector2);
			var s3 = Vector.CreateFromArray(surfaceBasisVector3);
			var s11 = Vector.CreateFromArray(surfaceBasisVectorDerivative1);
			var s22 = Vector.CreateFromArray(surfaceBasisVectorDerivative2);
			var s12 = Vector.CreateFromArray(surfaceBasisVectorDerivative12);
			for (int column = 0; column < controlPoints.Length * 3; column += 3)
			{
				#region BI1

				var BI1 = s3.CrossProduct(s3);
				BI1.ScaleIntoThis(nurbs.NurbsDerivativeValuesHeta[column / 3, j]);
				var auxVector = s2.CrossProduct(s3);
				auxVector.ScaleIntoThis(nurbs.NurbsDerivativeValuesKsi[column / 3, j]);
				BI1.AddIntoThis(auxVector);
				BI1.ScaleIntoThis(s3.DotProduct(s11));
				auxVector = s1.CrossProduct(s11);
				auxVector.ScaleIntoThis(nurbs.NurbsDerivativeValuesHeta[column / 3, j]);
				BI1.AddIntoThis(auxVector);
				BI1.ScaleIntoThis(1 / J1);
				auxVector[0] = surfaceBasisVector3[0];
				auxVector[1] = surfaceBasisVector3[1];
				auxVector[2] = surfaceBasisVector3[2];
				auxVector.ScaleIntoThis(-nurbs.NurbsSecondDerivativeValueKsi[column / 3, j]);
				BI1.AddIntoThis(auxVector);

				#endregion BI1

				#region BI2

				IVector BI2 = s3.CrossProduct(s3);
				BI2.ScaleIntoThis(nurbs.NurbsDerivativeValuesHeta[column / 3, j]);
				auxVector = s2.CrossProduct(s3);
				auxVector.ScaleIntoThis(nurbs.NurbsDerivativeValuesKsi[column / 3, j]);
				BI2.AddIntoThis(auxVector);
				BI2.ScaleIntoThis(s3.DotProduct(s22));
				auxVector = s1.CrossProduct(s22);
				auxVector.ScaleIntoThis(nurbs.NurbsDerivativeValuesHeta[column / 3, j]);
				BI2.AddIntoThis(auxVector);
				auxVector = s22.CrossProduct(s2);
				auxVector.ScaleIntoThis(nurbs.NurbsDerivativeValuesKsi[column / 3, j]);
				BI2.AddIntoThis(auxVector);
				BI2.ScaleIntoThis(1 / J1);
				auxVector[0] = surfaceBasisVector3[0];
				auxVector[1] = surfaceBasisVector3[1];
				auxVector[2] = surfaceBasisVector3[2];
				auxVector.ScaleIntoThis(-nurbs.NurbsSecondDerivativeValueHeta[column / 3, j]);
				BI2.AddIntoThis(auxVector);

				#endregion BI2

				#region BI3

				var BI3 = s3.CrossProduct(s3);
				BI3.ScaleIntoThis(nurbs.NurbsDerivativeValuesHeta[column / 3, j]);
				auxVector = s2.CrossProduct(s3);
				auxVector.ScaleIntoThis(nurbs.NurbsDerivativeValuesKsi[column / 3, j]);
				BI3.AddIntoThis(auxVector);
				BI3.ScaleIntoThis(s3.DotProduct(s12));
				auxVector = s1.CrossProduct(s12);
				auxVector.ScaleIntoThis(nurbs.NurbsDerivativeValuesHeta[column / 3, j]);
				BI3.AddIntoThis(auxVector);
				auxVector = s22.CrossProduct(s2);
				auxVector.ScaleIntoThis(nurbs.NurbsDerivativeValuesKsi[column / 3, j]);
				BI3.AddIntoThis(auxVector);
				BI3.ScaleIntoThis(1 / J1);
				auxVector[0] = surfaceBasisVector3[0];
				auxVector[1] = surfaceBasisVector3[1];
				auxVector[2] = surfaceBasisVector3[2];
				auxVector.ScaleIntoThis(-nurbs.NurbsSecondDerivativeValueKsiHeta[column / 3, j]);
				BI3.AddIntoThis(auxVector);

				#endregion BI3

				Bbending[0, column] = BI1[0];
				Bbending[0, column + 1] = BI1[1];
				Bbending[0, column + 2] = BI1[2];

				Bbending[1, column] = BI2[0];
				Bbending[1, column + 1] = BI2[1];
				Bbending[1, column + 2] = BI2[2];

				Bbending[2, column] = 2 * BI3[0];
				Bbending[2, column + 1] = 2 * BI3[1];
				Bbending[2, column + 2] = 2 * BI3[2];
			}
			return Bbending;
		}

		private double[] CalculateCrossProduct(double[] vector1, double[] vector2)
		{
			return new[]
			{
				vector1[1] * vector2[2] - vector1[2] * vector2[1],
				vector1[2] * vector2[0] - vector1[0] * vector2[2],
				vector1[0] * vector2[1] - vector1[1] * vector2[0]
			};
		}

		private double[][] initialSurfaceBasisVectors1;
		private double[][] initialSurfaceBasisVectors2;
		private double[][] initialSurfaceBasisVectors3;

		private double[][] initialSurfaceBasisVectorDerivative1;
		private double[][] initialSurfaceBasisVectorDerivative2;
		private double[][] initialSurfaceBasisVectorDerivative12;

		private double[] InitialJ1;

		private void CalculateInitialConfigurationData(ControlPoint[] controlPoints,
			Nurbs2D nurbs, IList<GaussLegendrePoint3D> gaussPoints)
		{
			var numberOfGP = gaussPoints.Count;
			InitialJ1=new double[numberOfGP];
			initialSurfaceBasisVectors1 = new double[numberOfGP][];
			initialSurfaceBasisVectors2 = new double[numberOfGP][];
			initialSurfaceBasisVectors3 = new double[numberOfGP][];
			initialSurfaceBasisVectorDerivative1 = new double[numberOfGP][];
			initialSurfaceBasisVectorDerivative2 = new double[numberOfGP][];
			initialSurfaceBasisVectorDerivative12 = new double[numberOfGP][];

			for (int j = 0; j < gaussPoints.Count; j++)
			{
				var jacobianMatrix = CalculateJacobian(controlPoints, nurbs, j);

				var hessianMatrix = CalculateHessian(controlPoints, nurbs, j);
				initialSurfaceBasisVectors1[j] = CalculateSurfaceBasisVector1(jacobianMatrix, 0);
				initialSurfaceBasisVectors2[j] = CalculateSurfaceBasisVector1(jacobianMatrix, 1);
				var s3= CalculateCrossProduct(initialSurfaceBasisVectors1[j], initialSurfaceBasisVectors2[j]);
				var norm = s3.Sum(t => t * t);
				InitialJ1[j] = Math.Sqrt(norm);
				initialSurfaceBasisVectors3[j] =
					CalculateCrossProduct(initialSurfaceBasisVectors1[j], initialSurfaceBasisVectors2[j]);

				initialSurfaceBasisVectorDerivative1[j] = CalculateSurfaceBasisVector1(hessianMatrix, 0);
				initialSurfaceBasisVectorDerivative2[j] = CalculateSurfaceBasisVector1(hessianMatrix, 1);
				initialSurfaceBasisVectorDerivative12[j] = CalculateSurfaceBasisVector1(hessianMatrix, 2);

				foreach (var integrationPointMaterial in materialsAtThicknessGP[gaussPoints[j]].Values)
				{
					integrationPointMaterial.TangentVectorV1 = initialSurfaceBasisVectors1[j];
					integrationPointMaterial.TangentVectorV2 = initialSurfaceBasisVectors2[j];
					integrationPointMaterial.NormalVectorV3 = initialSurfaceBasisVectors3[j];
				}
			}
		}

		private double[,] CalculateKbendingNL(ControlPoint[] controlPoints,
		   double[] bendingMoments, Nurbs2D nurbs, double[] surfaceBasisVector1,
		   double[] surfaceBasisVector2, double[] surfaceBasisVector3, 
		   double[] surfaceBasisVectorDerivative1, double[] surfaceBasisVectorDerivative2,
		   double[] surfaceBasisVectorDerivative12, double J1, int j)
		{
			var KbendingNL = new double[controlPoints.Length * 3, controlPoints.Length * 3];

			for (var i = 0; i < controlPoints.Length; i++)
			{
				var a1r = nurbs.NurbsDerivativeValuesKsi[i, j];
				var a2r = nurbs.NurbsDerivativeValuesHeta[i, j];

				var a11r = nurbs.NurbsSecondDerivativeValueKsi[i, j];
				var a22r = nurbs.NurbsSecondDerivativeValueHeta[i, j];
				var a12r = nurbs.NurbsSecondDerivativeValueKsiHeta[i, j];
				var a3r = CalculateA3r(nurbs.NurbsDerivativeValuesKsi[i, j], nurbs.NurbsDerivativeValuesHeta[i, j],
					surfaceBasisVector2, surfaceBasisVector1);

				for (var k = 0; k < controlPoints.Length; k++)
				{
					var a11s = nurbs.NurbsSecondDerivativeValueKsi[k, j];
					var a22s = nurbs.NurbsSecondDerivativeValueHeta[k, j];
					var a12s = nurbs.NurbsSecondDerivativeValueKsiHeta[k, j];
					var a3s = CalculateA3r(nurbs.NurbsDerivativeValuesKsi[k, j], nurbs.NurbsDerivativeValuesHeta[i, j],
						surfaceBasisVector2, surfaceBasisVector1);

					var a1s = nurbs.NurbsDerivativeValuesKsi[k, j];
					var a2s = nurbs.NurbsDerivativeValuesHeta[k, j];

					var termB = CalculateTermB(bendingMoments, J1, a1r, surfaceBasisVector2, surfaceBasisVector1, a2r, a1s, a2s, surfaceBasisVector3,
						surfaceBasisVectorDerivative1, surfaceBasisVectorDerivative2, surfaceBasisVectorDerivative12);
					var termA = CalculateTermA(bendingMoments, a11r, a3s, a11s, a3r, a22r, a22s, a12r, a12s);

					for (int l = 0; l < 3; l++)
					{
						for (int m = 0; m < 3; m++)
						{
							KbendingNL[i * 3 + l, k * 3 + m] += termA[l, m] + termB[l, m];
						}
					}
				}
			}

			return KbendingNL;
		}

		private double[,] CalculateTermA(double[] bendingMoments, double a11r, double[,] a3s, double a11s,
			double[,] a3r, double a22r, double a22s, double a12r, double a12s)
		{
			return new double[3, 3]
			{
				{
					0.0, bendingMoments[0] * (a11r * a3s[0, 1] + a11s * a3r[0, 1]) +
						 bendingMoments[1] * (a22r * a3s[0, 1] + a22s * a3r[0, 1]) +
						 2 * bendingMoments[2] * (a12r * a3s[0, 1] + a12s * a3r[0, 1]),
					bendingMoments[0] * (a11r * a3s[0, 2] + a11s * a3r[0, 2]) +
					bendingMoments[1] * (a22r * a3s[0, 2] + a22s * a3r[0, 2]) +
					2 * bendingMoments[2] * (a12r * a3s[0, 2] + a12s * a3r[0, 2])
				},
				{
					bendingMoments[0] * (a11r * a3s[1, 0] + a11s * a3r[1, 0]) +
					bendingMoments[1] * (a22r * a3s[1, 0] + a22s * a3r[1, 0]) +
					2 * bendingMoments[2] * (a12r * a3s[1, 0] + a12s * a3r[1, 0]),
					0.0,
					bendingMoments[0] * (a11r * a3s[1, 2] + a11s * a3r[1, 2]) +
					bendingMoments[1] * (a22r * a3s[1, 2] + a22s * a3r[1, 2]) +
					2 * bendingMoments[2] * (a12r * a3s[1, 2] + a12s * a3r[1, 2]),
				},
				{
					bendingMoments[0] * (a11r * a3s[2, 0] + a11s * a3r[2, 0]) +
					bendingMoments[1] * (a22r * a3s[2, 0] + a22s * a3r[2, 0]) +
					2 * bendingMoments[2] * (a12r * a3s[2, 0] + a12s * a3r[2, 0]),

					bendingMoments[0] * (a11r * a3s[2, 1] + a11s * a3r[2, 1]) +
					bendingMoments[1] * (a22r * a3s[2, 1] + a22s * a3r[2, 1]) +
					2 * bendingMoments[2] * (a12r * a3s[2, 1] + a12s * a3r[2, 1]),

					0.0
				},
			};
		}

		private double[,] CalculateTermB(double[] bendingMoments, double J1, double a1r, double[] s2, double[] s1, double a2r, double a1s,
			double a2s, double[] s3, double[] surfaceBasisVectorDerivative1, double[] surfaceBasisVectorDerivative2, double[] surfaceBasisVectorDerivative12)
		{
			var termB = new double[3, 3];
			var a3r_dashed = new double[3][];
			var a3s_dashed = new double[3][];

			a3r_dashed[0] = new double[] {0.0, -a1r * s2[2] + s1[2] * a2r, -a1r * s2[1] - s1[1] * a2r};
			a3r_dashed[1] = new double[] {a1r * s2[2] - s1[2] * a2r, 0.0, -a1r * s2[0] + s1[0] * a2r};
			a3r_dashed[2] = new double[] {-a1r * s2[1] + s1[1] * a2r, a1r * s2[0] - s1[0] * a2r, 0.0};

			a3s_dashed[0] = new double[] { 0.0, -a1s * s2[2] + s1[2] * a2s, -a1s * s2[1] - s1[1] * a2s };
			a3s_dashed[1] = new double[] { a1s * s2[2] - s1[2] * a2s, 0.0, -a1s * s2[0] + s1[0] * a2s };
			a3s_dashed[2] = new double[] { -a1s * s2[1] + s1[1] * a2s, a1s * s2[0] - s1[0] * a2s, 0.0 };


			var term_525s = new double[3];
			var term_525r = new double[3];

			term_525r[0] = s3[1] * a3r_dashed[0][1] + s3[2] * a3r_dashed[0][2];
			term_525r[1] = s3[0] * a3r_dashed[1][0] + s3[2] * a3r_dashed[1][2];
			term_525r[2] = s3[0] * a3r_dashed[2][0] + s3[1] * a3r_dashed[2][1];

			term_525s[0] = s3[1] * a3s_dashed[0][1] + s3[2] * a3s_dashed[0][2];
			term_525s[1] = s3[0] * a3s_dashed[1][0] + s3[2] * a3s_dashed[1][2];
			term_525s[2] = s3[0] * a3s_dashed[2][0] + s3[1] * a3s_dashed[2][1];

			var term1_532 = new double[3, 3][];
			var value = a1r * a2s*J1;
			term1_532[0, 0] = new double[3];
			term1_532[0, 1] = new double[3] {0, 0, value};
			term1_532[0, 2] = new double[3] {0, -value, 0};

			term1_532[1, 0] = new double[3] { 0, 0, -value };
			term1_532[1, 1] = new double[3];
			term1_532[1, 2] = new double[3] {value, 0, 0};

			term1_532[2, 0] = new double[3] {0, value, 0};
			term1_532[2, 1] = new double[3] {-value, 0, 0};
			term1_532[2, 2] = new double[3];


			for (int m = 0; m < 3; m++)
			{
				for (int n = 0; n < 3; n++)
				{
					var prod1 = -term_525s[n] / J1 / J1;
					var term2_532 = new double[]
					{
						a3r_dashed[m][0] * prod1, a3r_dashed[m][1] * prod1, a3r_dashed[m][2] * prod1,
					};
					var term3_532 = new double[]
					{
						a3s_dashed[n][0] * prod1, a3s_dashed[n][2] * prod1, a3s_dashed[n][1] * prod1
					};

					var a3_rs = CalculateA3_rs(s3, J1, term1_532[m,n], a3r_dashed[m], a3s_dashed[n]);
					var prod2 = -a3_rs / J1;
					var term4_532 = new double[] { s3[0] * prod2, s3[1] * prod2, s3[2] * prod2 };
					var prod3 = 2 / J1 / J1 * term_525r[m] * term_525s[n];
					var term5_532 = new double[] { s3[0] * prod3, s3[1] * prod3, s3[2] * prod3 };

					var a3rs = new double[]
					{
						term1_532[m,n][0] + term2_532[0] + term3_532[0] + term4_532[0] + term5_532[0],
						term1_532[m,n][1] + term2_532[1] + term3_532[1] + term4_532[1] + term5_532[1],
						term1_532[m,n][2] + term2_532[2] + term3_532[2] + term4_532[2] + term5_532[2],
					};

					var aux1 = surfaceBasisVectorDerivative1[0] * a3rs[0] + surfaceBasisVectorDerivative1[1] * a3rs[1] +
							   surfaceBasisVectorDerivative1[2] * a3rs[2];

					var aux2 = surfaceBasisVectorDerivative2[0] * a3rs[0] + surfaceBasisVectorDerivative2[1] * a3rs[1] +
							   surfaceBasisVectorDerivative2[2] * a3rs[2];

					var aux3 = surfaceBasisVectorDerivative12[0] * a3rs[0] +
							   surfaceBasisVectorDerivative12[1] * a3rs[1] +
							   surfaceBasisVectorDerivative12[2] * a3rs[2];

					termB[m, n] = bendingMoments[0] * aux1 +
								  bendingMoments[1] * aux2 +
								  2 * bendingMoments[2] * aux3;
				}
			}

			return termB;
		}

		private static double CalculateA3_rs(double[] s3, double J1, double[] term1_532, double[] a3r_dashed, double[] a3s_dashed)
		{
			var term1 = (term1_532[0] * s3[0] + term1_532[1] * s3[1] + term1_532[2] * s3[2]) * J1;
			var term2 = (a3r_dashed[0] * a3s_dashed[0] + a3r_dashed[1] * a3s_dashed[1] + a3r_dashed[2] * a3s_dashed[2]) / J1;
			var term3 = a3r_dashed[0] * s3[0] + a3r_dashed[1] * s3[1] + a3r_dashed[2] * s3[2];
			var term4 = a3s_dashed[0] * s3[0] + a3s_dashed[1] * s3[1] + a3s_dashed[2] * s3[2];

			var a3_rs = term1 + term2 - term3 * term4 / J1;
			return a3_rs;
		}

		private double[,] CreateDiagonal3by3WithValue(double value)
		{
			var matrix = new double[3, 3];
			matrix[0, 0] = value;
			matrix[1, 1] = value;
			matrix[2, 2] = value;
			return matrix;
		}

		private double[,] CalculateKmembraneNL(ControlPoint[] controlPoints, double[] membraneForces, Nurbs2D nurbs, int j)
		{
			var kmembraneNL = new double[controlPoints.Length * 3, controlPoints.Length * 3];

			for (var i = 0; i < controlPoints.Length; i++)
			{
				var dksiI = nurbs.NurbsDerivativeValuesKsi[i, j];
				var dHetaI = nurbs.NurbsDerivativeValuesHeta[i, j];
				for (var k = 0; k < controlPoints.Length; k++)
				{
					var dksiK = nurbs.NurbsDerivativeValuesKsi[k, j];
					var dHetaK = nurbs.NurbsDerivativeValuesHeta[k, j];

					var value = membraneForces[0] * (dksiI * dksiK) +
								membraneForces[1] * (dHetaI * dHetaK) +
								membraneForces[2] * (dksiI * dHetaK + dksiK * dHetaI);

					kmembraneNL[i * 3, k * 3] += value;
					kmembraneNL[i * 3 + 1, k * 3 + 1] += value;
					kmembraneNL[i * 3 + 2, k * 3 + 2] += value;
				}
			}

			return kmembraneNL;
		}

		private double[,] CalculateMembraneDeformationMatrix(ControlPoint[] controlPoints, Nurbs2D nurbs, int j,
			double[] surfaceBasisVector1,
			double[] surfaceBasisVector2)
		{
			var dRIa = new double[3, controlPoints.Length * 3];
			for (int i = 0; i < controlPoints.Length; i++)
			{
				for (int m = 0; m < 3; m++)
				{
					dRIa[m, i] = nurbs.NurbsDerivativeValuesHeta[i, j] * surfaceBasisVector1[m] +
								 nurbs.NurbsDerivativeValuesKsi[i, j] * surfaceBasisVector2[m];
				}
			}

			var bmembrane = new double[3, controlPoints.Length * 3];
			for (int column = 0; column < controlPoints.Length * 3; column += 3)
			{
				bmembrane[0, column] = nurbs.NurbsDerivativeValuesKsi[column / 3, j] * surfaceBasisVector1[0];
				bmembrane[0, column + 1] = nurbs.NurbsDerivativeValuesKsi[column / 3, j] * surfaceBasisVector1[1];
				bmembrane[0, column + 2] = nurbs.NurbsDerivativeValuesKsi[column / 3, j] * surfaceBasisVector1[2];

				bmembrane[1, column] = nurbs.NurbsDerivativeValuesHeta[column / 3, j] * surfaceBasisVector2[0];
				bmembrane[1, column + 1] = nurbs.NurbsDerivativeValuesHeta[column / 3, j] * surfaceBasisVector2[1];
				bmembrane[1, column + 2] = nurbs.NurbsDerivativeValuesHeta[column / 3, j] * surfaceBasisVector2[2];

				bmembrane[2, column] = dRIa[0, column / 3];
				bmembrane[2, column + 1] = dRIa[1, column / 3];
				bmembrane[2, column + 2] = dRIa[2, column / 3];
			}

			return bmembrane;
		}

		private double[,] CopyConstitutiveMatrix(double[,] f)
		{
			var g = new double[f.GetLength(0), f.GetLength(1)];
			Array.Copy(f, 0, g, 0, f.Length);
			return g;
		}

		private IList<GaussLegendrePoint3D> CreateElementGaussPoints(NurbsKirchhoffLoveShellElementNL shellElement)
		{
			var gauss = new GaussQuadrature();
			var medianSurfaceGP = gauss.CalculateElementGaussPoints(shellElement.Patch.DegreeKsi, shellElement.Patch.DegreeHeta, shellElement.Knots.ToList());

			foreach (var point in medianSurfaceGP)
			{
				var gp = gauss.CalculateElementGaussPoints(ThicknessIntegrationDegree,
					new List<Knot>
					{
						new Knot() {ID = 0, Ksi = -shellElement.Thickness / 2, Heta = point.Heta},
						new Knot() {ID = 1, Ksi = shellElement.Thickness / 2, Heta = point.Heta},
					}).ToList();

				thicknessIntegrationPoints.Add(point,
					gp.Select(g => new GaussLegendrePoint3D(point.Ksi, point.Heta, g.Ksi, g.WeightFactor))
						.ToList());
			}

			return medianSurfaceGP;
		}

		private const int ThicknessIntegrationDegree = 2;

		public double Thickness { get; set; }
	}
}
