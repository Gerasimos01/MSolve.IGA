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

	/// <summary>
	/// An shell element that utilizes Non-Uniform Rational B-Splines for shape functions.
	/// It is based on Kirchhoff-Love theory. Geometrically linear formulation.
	/// For more information please refer to <see href="https://www.sciencedirect.com/science/article/pii/S0045782509002680"/>
	/// Authors: Dimitris Tsapetis.
	/// </summary>
	public class NurbsKirchhoffLoveShellElement : Element, IStructuralIsogeometricElement, ISurfaceLoadedElement
	{
		protected static readonly IDofType[] ControlPointDofTypes = { StructuralDof.TranslationX, StructuralDof.TranslationY, StructuralDof.TranslationZ };
		private IDofType[][] dofTypes;

		/// <summary>
		/// Retrieves the type of Finite Element used. Since the element is Isogeometric its type is defined as unknown.
		/// </summary>
		public CellType CellType { get; } = CellType.Unknown;

		/// <summary>
		/// Defines the way that elemental degrees of freedom will be enumerated.
		/// For further info see <see cref="IElementDofEnumerator"/>.
		/// </summary>
		public IElementDofEnumerator DofEnumerator { get; set; } = new GenericDofEnumerator();

		/// <summary>
		/// Retrieves the number of Dimensions of the element.
		/// </summary>
		public ElementDimensions ElementDimensions => ElementDimensions.ThreeD;

		/// <summary>
		/// Boolean property that determines whether the material used for this elements has been modified.
		/// </summary>
		public bool MaterialModified => throw new NotImplementedException();

		/// <summary>
		/// Calculates the forces applies to an <see cref="NurbsKirchhoffLoveShellElement"/> due to <see cref="MassAccelerationLoad"/>.
		/// </summary>
		/// <param name="element">An element of type <see cref="NurbsKirchhoffLoveShellElement"/>.</param>
		/// <param name="loads">A list of <see cref="MassAccelerationLoad"/>. For more info see <seealso cref="MassAccelerationLoad"/>.</param>
		/// <returns>A <see cref="double"/> array containing the forces generates due to acceleration for each degree of freedom.</returns>
		public double[] CalculateAccelerationForces(IElement element, IList<MassAccelerationLoad> loads) => throw new NotImplementedException();

		/// <summary>
		/// Calculates displacements of knots for post-processing with Paraview.
		/// </summary>
		/// <param name="element">An element of type <see cref="NurbsKirchhoffLoveShellElement"/>.</param>
		/// <param name="localDisplacements">A <see cref="Matrix"/> containing the displacements for the degrees of freedom of the element.</param>
		/// <returns>A <see cref="double"/> array calculating the displacement of the element Knots'.
		/// The rows of the matrix denote the knot numbering while the columns the displacements for each degree of freedom.</returns>
		public double[,] CalculateDisplacementsForPostProcessing(Element element, Matrix localDisplacements)
		{
			var nurbsElement = (NurbsKirchhoffLoveShellElement)element;
			var elementControlPoints = nurbsElement.ControlPoints.ToArray();
			var elementKnots = nurbsElement.Knots.ToArray();
			var knotParametricCoordinatesKsi = Vector.CreateFromArray(new double[] { elementKnots[0].Ksi, elementKnots[2].Ksi });
			var knotParametricCoordinatesHeta = Vector.CreateFromArray(new double[] { elementKnots[0].Heta, elementKnots[1].Heta });
			Nurbs2D nurbs = new Nurbs2D(nurbsElement, elementControlPoints, knotParametricCoordinatesKsi, knotParametricCoordinatesHeta);
			var knotDisplacements = new double[4, 3];
			var paraviewKnotRenumbering = new int[] { 0, 3, 1, 2 };
			for (int j = 0; j < elementKnots.Length; j++)
			{
				for (int i = 0; i < elementControlPoints.Length; i++)
				{
					knotDisplacements[paraviewKnotRenumbering[j], 0] += nurbs.NurbsValues[i, j] * localDisplacements[i, 0];
					knotDisplacements[paraviewKnotRenumbering[j], 1] += nurbs.NurbsValues[i, j] * localDisplacements[i, 1];
					knotDisplacements[paraviewKnotRenumbering[j], 2] += nurbs.NurbsValues[i, j] * localDisplacements[i, 2];
				}
			}

			return knotDisplacements;
		}

		/// <summary>
		/// This method calculates the internal forces of the element.
		/// </summary>
		/// <param name="element">An element of type <see cref="NurbsKirchhoffLoveShellElement"/></param>
		/// <param name="localDisplacements">A <see cref="double"/> array containing the displacements for the degrees of freedom of the element.</param>
		/// <param name="localdDisplacements">A <see cref="double"/> array containing the displacements change for the degrees of freedom of the element.</param>
		/// <returns>A <see cref="double"/> array containing the forces all degrees of freedom</returns>
		public double[] CalculateForces(IElement element, double[] localDisplacements, double[] localdDisplacements) => throw new NotImplementedException();

		/// <summary>
		/// This method is used for retrieving the internal forces of the element for logging purposes.
		/// </summary>
		/// <param name="element">An element of type <see cref="NurbsKirchhoffLoveShellElement"/></param>
		/// <param name="localDisplacements">A <see cref="double"/> array containing the displacements for the degrees of freedom of the element.</param>
		/// <returns>A <see cref="double"/> array containing the forces all degrees of freedom</returns>
		public double[] CalculateForcesForLogging(IElement element, double[] localDisplacements) => throw new NotImplementedException();

		/// <summary>
		/// This method cannot be used, combined with <see cref="NurbsKirchhoffLoveShellElement"/> as it refers to one-dimensional loads.
		/// </summary>
		/// <param name="element">An element of type <see cref="NurbsKirchhoffLoveShellElement"/>.</param>
		/// <param name="edge">An one dimensional boundary entity. For more info see <see cref="Edge"/>.</param>
		/// <param name="neumann"><inheritdoc cref="NeumannBoundaryCondition"/></param>
		/// <returns>A <see cref="Dictionary{TKey,TValue}"/> where integer values denote the degree of freedom that has a value double load value due to the enforcement of the <see cref="NeumannBoundaryCondition"/>.</returns>
		public Dictionary<int, double> CalculateLoadingCondition(Element element, Edge edge, NeumannBoundaryCondition neumann) => throw new NotImplementedException();

		/// <summary>
		/// This method cannot be used, combined with <see cref="NurbsKirchhoffLoveShellElement"/> as it refers to two-dimensional loads.
		/// </summary>
		/// <param name="element">An <see cref="Element"/> of type <see cref="NurbsKirchhoffLoveShellElement"/>.</param>
		/// <param name="face">The <see cref="Face"/> that the <see cref="NeumannBoundaryCondition"/> was applied to.</param>
		/// <param name="neumann">The <see cref="NeumannBoundaryCondition"/>.</param>
		/// <returns>A <see cref="Dictionary{TKey,TValue}"/> whose keys are the numbering of the degree of freedom and values are the magnitude of the load due to the <see cref="NeumannBoundaryCondition"/>.</returns>
		public Dictionary<int, double> CalculateLoadingCondition(Element element, Face face, NeumannBoundaryCondition neumann) => throw new NotImplementedException();

		/// <summary>
		/// This method cannot be used, combined with <see cref="NurbsKirchhoffLoveShellElement"/> as it refers to one-dimensional loads.
		/// </summary>
		/// <param name="element">An element of type <see cref="NurbsKirchhoffLoveShellElement"/>.</param>
		/// <param name="edge">An one dimensional boundary entity. For more info see <see cref="Edge"/>.</param>
		/// <param name="pressure"><inheritdoc cref="PressureBoundaryCondition"/></param>
		/// <returns>A <see cref="Dictionary{TKey,TValue}"/> where integer values denote the degree of freedom that has a value double load value due to the enforcement of the <see cref="PressureBoundaryCondition"/>.</returns>
		public Dictionary<int, double> CalculateLoadingCondition(Element element, Edge edge, PressureBoundaryCondition pressure) => throw new NotImplementedException();

		/// <summary>
		/// This method cannot be used, combined with <see cref="NurbsKirchhoffLoveShellElement"/> as it refers to two-dimensional loads.
		/// </summary>
		/// <param name="element">An <see cref="Element"/> of type <see cref="NurbsKirchhoffLoveShellElement"/>.</param>
		/// <param name="face">The <see cref="Face"/> that the <see cref="PressureBoundaryCondition"/> was applied to.</param>
		/// <param name="pressure">The <see cref="PressureBoundaryCondition"/>.</param>
		/// <returns>A <see cref="Dictionary{TKey,TValue}"/> whose keys are the numbering of the degree of freedom and values are the magnitude of the load due to the <see cref="PressureBoundaryCondition"/>.</returns>
		public Dictionary<int, double> CalculateLoadingCondition(Element element, Face face, PressureBoundaryCondition pressure) => throw new NotImplementedException();

		/// <summary>
		/// This method calculates the stresses of the element.
		/// </summary>
		/// <param name="element">An element of type <see cref="NurbsKirchhoffLoveShellElement"/>.</param>
		/// <param name="localDisplacements">A <see cref="double"/> array containing the displacements for the degrees of freedom of the element.</param>
		/// <param name="localdDisplacements">A <see cref="double"/> array containing the displacements change for the degrees of freedom of the element.</param>
		/// <returns>A <see cref="Tuple{T1,T2}"/> of the stresses and strains of the element.</returns>
		public Tuple<double[], double[]> CalculateStresses(IElement element, double[] localDisplacements, double[] localdDisplacements) => throw new NotImplementedException();

		/// <summary>
		/// Clear the material state of the element.
		/// </summary>
		public void ClearMaterialState() => throw new NotImplementedException();

		/// <summary>
		/// Clear any saved material states of the element.
		/// </summary>
		public void ClearMaterialStresses() => throw new NotImplementedException();

		/// <summary>
		/// Calculates the damping matrix of the element.
		/// </summary>
		/// <param name="element">An element of type <see cref="NurbsKirchhoffLoveShellElement"/>.</param>
		/// <returns>An <see cref="IMatrix"/> containing the damping matrix of a <see cref="NurbsKirchhoffLoveShellElement"/>.</returns>
		public IMatrix DampingMatrix(IElement element) => throw new NotImplementedException();

		/// <summary>
		/// Retrieves the dofs of the element.
		/// </summary>
		/// <param name="element">An element of type <see cref="NurbsKirchhoffLoveShellElement"/>.</param>
		/// <returns>A <see cref="IReadOnlyList{T}"/> that contains a <see cref="IReadOnlyList{T}"/> of <see cref="IDofType"/> with degrees of freedom for each elemental <see cref="ControlPoint"/>.</returns>
		public IReadOnlyList<IReadOnlyList<IDofType>> GetElementDofTypes(IElement element)
		{
			var nurbsElement = (NurbsKirchhoffLoveShellElement)element;
			dofTypes = new IDofType[nurbsElement.ControlPointsDictionary.Count][];
			for (int i = 0; i < nurbsElement.ControlPointsDictionary.Count; i++)
			{
				dofTypes[i] = ControlPointDofTypes;
			}

			return dofTypes;
		}

		/// <summary>
		/// Calculates the mass matrix of the element.
		/// </summary>
		/// <param name="element">An element of type <see cref="NurbsKirchhoffLoveShellElement"/>.</param>
		/// <returns>An <see cref="IMatrix"/> containing the mass matrix of an <see cref="NurbsKirchhoffLoveShellElement"/>.</returns>
		public IMatrix MassMatrix(IElement element) => throw new NotImplementedException();

		/// <summary>
		/// Resets any saved material states of the element to its initial state.
		/// </summary>
		public void ResetMaterialModified() => throw new NotImplementedException();

		/// <summary>
		/// Save the current material state of the element.
		/// </summary>
		public void SaveMaterialState() => throw new NotImplementedException();

		/// <summary>
		/// Calculates the stiffness matrix of the element.
		/// </summary>
		/// <param name="element">An element of type <see cref="NurbsKirchhoffLoveShellElement"/>.</param>
		/// <returns>An <see cref="IMatrix"/> containing the stiffness matrix of an <see cref="NurbsKirchhoffLoveShellElement"/>.</returns>
		public IMatrix StiffnessMatrix(IElement element)
		{
			var shellElement = (NurbsKirchhoffLoveShellElement)element;
			var elementControlPoints = shellElement.ControlPoints.ToArray();
			IList<GaussLegendrePoint3D> gaussPoints = CreateElementGaussPoints(shellElement);
			Matrix stiffnessMatrixElement = Matrix.CreateZero(shellElement.ControlPointsDictionary.Count * 3, shellElement.ControlPointsDictionary.Count * 3);

			Nurbs2D nurbs = new Nurbs2D(shellElement, elementControlPoints);

			for (int j = 0; j < gaussPoints.Count; j++)
			{
				var jacobianMatrix = CalculateJacobian(shellElement, nurbs, j);

				var hessianMatrix = CalculateHessian(shellElement, nurbs, j);

				var surfaceBasisVector1 = CalculateSurfaceBasisVector1(jacobianMatrix, 0);

				var surfaceBasisVector2 = CalculateSurfaceBasisVector1(jacobianMatrix, 1);

				var surfaceBasisVector3 = surfaceBasisVector1.CrossProduct(surfaceBasisVector2);
				var J1 = surfaceBasisVector3.Norm2();
				surfaceBasisVector3.ScaleIntoThis(1 / J1);

				var surfaceBasisVectorDerivative1 = CalculateSurfaceBasisVector1(hessianMatrix, 0);
				var surfaceBasisVectorDerivative2 = CalculateSurfaceBasisVector1(hessianMatrix, 1);
				var surfaceBasisVectorDerivative12 = CalculateSurfaceBasisVector1(hessianMatrix, 2);

				Matrix constitutiveMatrix = CalculateConstitutiveMatrix(shellElement, surfaceBasisVector1, surfaceBasisVector2);

				var Bmembrane = CalculateMembraneDeformationMatrix(nurbs, j, surfaceBasisVector1, surfaceBasisVector2, shellElement);

				var Bbending = CalculateBendingDeformationMatrix(surfaceBasisVector3, nurbs, j, surfaceBasisVector2, surfaceBasisVectorDerivative1, surfaceBasisVector1, J1, surfaceBasisVectorDerivative2, surfaceBasisVectorDerivative12, shellElement);

				double membraneStiffness = ((IIsotropicContinuumMaterial2D)shellElement.Patch.Material).YoungModulus * shellElement.Patch.Thickness /
										   (1 - Math.Pow(((IIsotropicContinuumMaterial2D)shellElement.Patch.Material).PoissonRatio, 2));

				var Kmembrane = Bmembrane.Transpose() * constitutiveMatrix * Bmembrane * membraneStiffness * J1 *
								gaussPoints[j].WeightFactor;

				double bendingStiffness = ((IIsotropicContinuumMaterial2D)shellElement.Patch.Material).YoungModulus * Math.Pow(shellElement.Patch.Thickness, 3) /
										  12 / (1 - Math.Pow(((IIsotropicContinuumMaterial2D)shellElement.Patch.Material).PoissonRatio, 2));

				var Kbending = Bbending.Transpose() * constitutiveMatrix * Bbending * bendingStiffness * J1 *
							   gaussPoints[j].WeightFactor;

				stiffnessMatrixElement.AddIntoThis(Kmembrane);
				stiffnessMatrixElement.AddIntoThis(Kbending);
			}
			return stiffnessMatrixElement;
		}

		private static Matrix CalculateHessian(NurbsKirchhoffLoveShellElement shellElement, Nurbs2D nurbs, int j)
		{
			var elementControlPoints = shellElement.ControlPoints.ToArray();
			Matrix hessianMatrix = Matrix.CreateZero(3, 3);
			for (int k = 0; k < elementControlPoints.Length; k++)
			{
				hessianMatrix[0, 0] += nurbs.NurbsSecondDerivativeValueKsi[k, j] * elementControlPoints[k].X;
				hessianMatrix[0, 1] += nurbs.NurbsSecondDerivativeValueKsi[k, j] * elementControlPoints[k].Y;
				hessianMatrix[0, 2] += nurbs.NurbsSecondDerivativeValueKsi[k, j] * elementControlPoints[k].Z;
				hessianMatrix[1, 0] += nurbs.NurbsSecondDerivativeValueHeta[k, j] * elementControlPoints[k].X;
				hessianMatrix[1, 1] += nurbs.NurbsSecondDerivativeValueHeta[k, j] * elementControlPoints[k].Y;
				hessianMatrix[1, 2] += nurbs.NurbsSecondDerivativeValueHeta[k, j] * elementControlPoints[k].Z;
				hessianMatrix[2, 0] += nurbs.NurbsSecondDerivativeValueKsiHeta[k, j] * elementControlPoints[k].X;
				hessianMatrix[2, 1] += nurbs.NurbsSecondDerivativeValueKsiHeta[k, j] * elementControlPoints[k].Y;
				hessianMatrix[2, 2] += nurbs.NurbsSecondDerivativeValueKsiHeta[k, j] * elementControlPoints[k].Z;
			}

			return hessianMatrix;
		}

		private static Matrix CalculateJacobian(NurbsKirchhoffLoveShellElement shellElement, Nurbs2D nurbs, int j)
		{
			var elementControlPoints = shellElement.ControlPoints.ToArray();
			Matrix jacobianMatrix = Matrix.CreateZero(2, 3);
			for (int k = 0; k < elementControlPoints.Length; k++)
			{
				jacobianMatrix[0, 0] += nurbs.NurbsDerivativeValuesKsi[k, j] * elementControlPoints[k].X;
				jacobianMatrix[0, 1] += nurbs.NurbsDerivativeValuesKsi[k, j] * elementControlPoints[k].Y;
				jacobianMatrix[0, 2] += nurbs.NurbsDerivativeValuesKsi[k, j] * elementControlPoints[k].Z;
				jacobianMatrix[1, 0] += nurbs.NurbsDerivativeValuesHeta[k, j] * elementControlPoints[k].X;
				jacobianMatrix[1, 1] += nurbs.NurbsDerivativeValuesHeta[k, j] * elementControlPoints[k].Y;
				jacobianMatrix[1, 2] += nurbs.NurbsDerivativeValuesHeta[k, j] * elementControlPoints[k].Z;
			}

			return jacobianMatrix;
		}

		private static Vector CalculateSurfaceBasisVector1(Matrix Matrix, int row)
		{
			Vector surfaceBasisVector1 = Vector.CreateZero(3);
			surfaceBasisVector1[0] = Matrix[row, 0];
			surfaceBasisVector1[1] = Matrix[row, 1];
			surfaceBasisVector1[2] = Matrix[row, 2];
			return surfaceBasisVector1;
		}

		private Matrix CalculateBendingDeformationMatrix(Vector surfaceBasisVector3, Nurbs2D nurbs, int j,
			Vector surfaceBasisVector2, Vector surfaceBasisVectorDerivative1, Vector surfaceBasisVector1, double J1,
			Vector surfaceBasisVectorDerivative2, Vector surfaceBasisVectorDerivative12, NurbsKirchhoffLoveShellElement element)
		{
			var elementControlPoints = element.ControlPoints.ToArray();
			Matrix Bbending = Matrix.CreateZero(3, elementControlPoints.Length * 3);
			for (int column = 0; column < elementControlPoints.Length * 3; column += 3)
			{
				#region BI1

				var BI1 = surfaceBasisVector3.CrossProduct(surfaceBasisVector3);
				BI1.ScaleIntoThis(nurbs.NurbsDerivativeValuesHeta[column / 3, j]);
				var auxVector = surfaceBasisVector2.CrossProduct(surfaceBasisVector3);
				auxVector.ScaleIntoThis(nurbs.NurbsDerivativeValuesKsi[column / 3, j]);
				BI1.AddIntoThis(auxVector);
				
				BI1.ScaleIntoThis(surfaceBasisVector3.DotProduct(surfaceBasisVectorDerivative1));
				auxVector = surfaceBasisVector1.CrossProduct(surfaceBasisVectorDerivative1);
				auxVector.ScaleIntoThis(nurbs.NurbsDerivativeValuesHeta[column / 3, j]);
				BI1.AddIntoThis(auxVector);

				auxVector = surfaceBasisVectorDerivative1.CrossProduct(surfaceBasisVector2);
				auxVector.ScaleIntoThis(nurbs.NurbsDerivativeValuesKsi[column / 3, j]);
				BI1.AddIntoThis(auxVector);

				BI1.ScaleIntoThis(1 / J1);
				auxVector[0] = surfaceBasisVector3[0];
				auxVector[1] = surfaceBasisVector3[1];
				auxVector[2] = surfaceBasisVector3[2];
				auxVector.ScaleIntoThis(-nurbs.NurbsSecondDerivativeValueKsi[column / 3, j]);
				BI1.AddIntoThis(auxVector);

				#endregion BI1

				#region BI2

				Vector BI2 = surfaceBasisVector3.CrossProduct(surfaceBasisVector3);
				BI2.ScaleIntoThis(nurbs.NurbsDerivativeValuesHeta[column / 3, j]);
				auxVector = surfaceBasisVector2.CrossProduct(surfaceBasisVector3);
				auxVector.ScaleIntoThis(nurbs.NurbsDerivativeValuesKsi[column / 3, j]);
				BI2.AddIntoThis(auxVector);
				BI2.ScaleIntoThis(surfaceBasisVector3.DotProduct(surfaceBasisVectorDerivative2));
				auxVector = surfaceBasisVector1.CrossProduct(surfaceBasisVectorDerivative2);
				auxVector.ScaleIntoThis(nurbs.NurbsDerivativeValuesHeta[column / 3, j]);
				BI2.AddIntoThis(auxVector);
				auxVector = surfaceBasisVectorDerivative2.CrossProduct(surfaceBasisVector2);
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

				Vector BI3 = surfaceBasisVector3.CrossProduct(surfaceBasisVector3);
				BI3.ScaleIntoThis(nurbs.NurbsDerivativeValuesHeta[column / 3, j]);
				auxVector = surfaceBasisVector2.CrossProduct(surfaceBasisVector3);
				auxVector.ScaleIntoThis(nurbs.NurbsDerivativeValuesKsi[column / 3, j]);
				BI3.AddIntoThis(auxVector);
				BI3.ScaleIntoThis(surfaceBasisVector3.DotProduct(surfaceBasisVectorDerivative12));
				auxVector = surfaceBasisVector1.CrossProduct(surfaceBasisVectorDerivative12);
				auxVector.ScaleIntoThis(nurbs.NurbsDerivativeValuesHeta[column / 3, j]);
				BI3.AddIntoThis(auxVector);
				auxVector = surfaceBasisVectorDerivative2.CrossProduct(surfaceBasisVector2);
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

		private Matrix CalculateConstitutiveMatrix(NurbsKirchhoffLoveShellElement element, Vector surfaceBasisVector1, Vector surfaceBasisVector2)
		{
			var auxMatrix1 = Matrix.CreateZero(2, 2);
			auxMatrix1[0, 0] = surfaceBasisVector1.DotProduct(surfaceBasisVector1);
			auxMatrix1[0, 1] = surfaceBasisVector1.DotProduct(surfaceBasisVector2);
			auxMatrix1[1, 0] = surfaceBasisVector2.DotProduct(surfaceBasisVector1);
			auxMatrix1[1, 1] = surfaceBasisVector2.DotProduct(surfaceBasisVector2);
			(Matrix inverse, double det) = auxMatrix1.InvertAndDeterminant();

			var material = ((IContinuumMaterial2D)element.Patch.Material);
			var constitutiveMatrix = Matrix.CreateFromArray(new double[3, 3]
			{
				{
					inverse[0,0]*inverse[0,0],
					material.PoissonRatio*inverse[0,0]*inverse[1,1]+(1-material.PoissonRatio)*inverse[1,0]*inverse[1,0],
					inverse[0,0]*inverse[1,0]
				},
				{
					material.PoissonRatio*inverse[0,0]*inverse[1,1]+(1-material.PoissonRatio)*inverse[1,0]*inverse[1,0],
					inverse[1,1]*inverse[1,1],
					inverse[1,1]*inverse[1,0]
				},
				{
					inverse[0,0]*inverse[1,0],
					inverse[1,1]*inverse[1,0],
					0.5*(1-material.PoissonRatio)*inverse[0,0]*inverse[1,1]+(1+material.PoissonRatio)*inverse[1,0]*inverse[1,0]
				},
			});
			return constitutiveMatrix;
		}

		private Matrix CalculateMembraneDeformationMatrix(Nurbs2D nurbs, int j, Vector surfaceBasisVector1,
			Vector surfaceBasisVector2, NurbsKirchhoffLoveShellElement element)
		{
			var elementControlPoints = element.ControlPoints.ToArray();
			Matrix dRIa = Matrix.CreateZero(3, elementControlPoints.Length);
			for (int i = 0; i < elementControlPoints.Length; i++)
			{
				for (int m = 0; m < 3; m++)
				{
					dRIa[m, i] = nurbs.NurbsDerivativeValuesHeta[i, j] * surfaceBasisVector1[m] +
								 nurbs.NurbsDerivativeValuesKsi[i, j] * surfaceBasisVector2[m];
				}
			}

			Matrix Bmembrane = Matrix.CreateZero(3, elementControlPoints.Length * 3);
			for (int column = 0; column < elementControlPoints.Length * 3; column += 3)
			{
				Bmembrane[0, column] = nurbs.NurbsDerivativeValuesKsi[column / 3, j] * surfaceBasisVector1[0];
				Bmembrane[0, column + 1] = nurbs.NurbsDerivativeValuesKsi[column / 3, j] * surfaceBasisVector1[1];
				Bmembrane[0, column + 2] = nurbs.NurbsDerivativeValuesKsi[column / 3, j] * surfaceBasisVector1[2];

				Bmembrane[1, column] = nurbs.NurbsDerivativeValuesHeta[column / 3, j] * surfaceBasisVector2[0];
				Bmembrane[1, column + 1] = nurbs.NurbsDerivativeValuesHeta[column / 3, j] * surfaceBasisVector2[1];
				Bmembrane[1, column + 2] = nurbs.NurbsDerivativeValuesHeta[column / 3, j] * surfaceBasisVector2[2];

				Bmembrane[2, column] = dRIa[0, column / 3];
				Bmembrane[2, column + 1] = dRIa[1, column / 3];
				Bmembrane[2, column + 2] = dRIa[2, column / 3];
			}

			return Bmembrane;
		}

		private IList<GaussLegendrePoint3D> CreateElementGaussPoints(Element element)
		{
			GaussQuadrature gauss = new GaussQuadrature();
			return gauss.CalculateElementGaussPoints(element.Patch.DegreeKsi, element.Patch.DegreeHeta, element.Knots.ToArray());
		}

		public Dictionary<int, double> CalculateSurfacePressure(Element element, double pressureMagnitude)
		{
			var shellElement = (NurbsKirchhoffLoveShellElement)element;
			var elementControlPoints = shellElement.ControlPoints.ToArray();
			var gaussPoints = CreateElementGaussPoints(shellElement);
			var pressureLoad = new Dictionary<int, double>();
			var nurbs = new Nurbs2D(shellElement, elementControlPoints);

			for (var j = 0; j < gaussPoints.Count; j++)
			{
				var jacobianMatrix = CalculateJacobian(shellElement, nurbs, j);
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

		public Dictionary<int, double> CalculateSurfaceDistributedLoad(Element element, IDofType loadedDof, double loadMagnitude)
		{
			var shellElement = (NurbsKirchhoffLoveShellElement)element;
			var elementControlPoints = shellElement.ControlPoints.ToArray();
			var gaussPoints = CreateElementGaussPoints(shellElement);
			var distributedLoad = new Dictionary<int, double>();
			var nurbs = new Nurbs2D(shellElement, elementControlPoints);

			for (var j = 0; j < gaussPoints.Count; j++)
			{
				var jacobianMatrix = CalculateJacobian(shellElement, nurbs, j);
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
	}
}
